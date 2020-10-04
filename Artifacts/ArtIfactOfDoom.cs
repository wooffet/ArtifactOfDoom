
using ArtifactOfDoom.Artifacts.Contracts;
using ArtifactOfDoom.Ui.Contracts;
using R2API;
using RoR2;
using RoR2.Stats;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using TILER2;
using TinyJson;

namespace ArtifactOfDoom
{
    public class ArtifactOfDoom : Artifact<ArtifactOfDoom>
    {
        // Following the C# at Google Style Guide for naming conventions, see: https://google.github.io/styleguide/csharp-style.html
        // NOTE: Can be excluded if needed, I'm personally trying to get better with naming conventions after using too many different styles in differnt workplaces
        // NOTE: Can't change name of overridden properties
        private const string _grayColor = "7e91af";
        public static bool Debug = false;
        public override string displayName => "Artifact of Doom";

        protected override string NewLangName(string langid = null) => displayName;
        protected override string NewLangDesc(string langid = null) => "You get items on enemy kills but lose items every time you take damage.";
        private static List<CharacterBody> _playerNames;
        private static List<double> _counters;
        private int _currentStage;

        private Dictionary<NetworkUser, bool> _lockNetworkUser = new Dictionary<NetworkUser, bool>();
        private Dictionary<NetworkUser, bool> _lockItemGainNetworkUser = new Dictionary<NetworkUser, bool>();

        private static StatDef _statsLostItems;
        private static StatDef _statsGainItems;
        
        public static Dictionary<uint, Queue<ItemDef>> QueueLostItemSprite = new Dictionary<uint, Queue<ItemDef>>();
        public static Dictionary<uint, Queue<ItemDef>> QueueGainedItemSprite = new Dictionary<uint, Queue<ItemDef>>();

        private static double _timeForBuff = 0.0;

        private static readonly float _baseDropChance = 5f * (float)ArtifactOfDoomConfig.multiplierForArtifactOfSacrificeDropRate.Value;

        public ArtifactOfDoom()
        {
            iconPathName = "@ArtifactOfDoom:Assets/Import/artifactofdoom_icon/ArtifactDoomEnabled.png";
            iconPathNameDisabled = "@ArtifactOfDoom:Assets/Import/artifactofdoom_icon/ArtifactDoomDisabled.png";
        }

        public void Awake()
        {
            if (IsActiveAndEnabled())
            {
                Chat.AddMessage($"Loaded {displayName}!");
                LoadBehavior();
            }
        }
        
        protected override void LoadBehavior()
        {
            _playerNames = new List<CharacterBody>();
            _counters = new List<double>();
            _currentStage = 0;

            _statsLostItems = null;
            _statsGainItems = null;

            _statsLostItems = StatDef.Register("Lostitems", StatRecordType.Sum, StatDataType.ULong, 0, null);
            _statsGainItems = StatDef.Register("Gainitems", StatRecordType.Sum, StatDataType.ULong, 0, null);

            RegisterGameEvents();
        }

        private void RegisterGameEvents()
        {
            RegisterGameEndReportPanelControllerAwakeEvent();
            RegisterSceneDirectorPopulateSceneEvent();
            RegisterRunStartEvent();
            RegisterCharacterBodyOnInventoryChangedEvent();
            RegisterGlobalEventManagerOnCharacterDeathEvent();
            RegisterHealthComponentTakeDamageEvent();
        }

        private void RegisterGameEndReportPanelControllerAwakeEvent()
        {
            On.RoR2.UI.GameEndReportPanelController.Awake += (orig, self) =>
            {
                orig(self);
                if (!IsActiveAndEnabled())
                {
                    return;
                }
                string[] information = new string[self.statsToDisplay.Length + 2];
                self.statsToDisplay.CopyTo(information, 0);
                information[information.Length - 2] = "Lostitems";
                information[information.Length - 1] = "Gainitems";
                self.statsToDisplay = information;
            };
        }

        private void RegisterSceneDirectorPopulateSceneEvent()
        {
            On.RoR2.SceneDirector.PopulateScene += (orig, self) =>
            {
                orig(self);
                _currentStage = Run.instance.stageClearCount + 1;

                switch (Run.instance.selectedDifficulty)
                {
                    case DifficultyIndex.Easy:
                        _timeForBuff = ArtifactOfDoomConfig.timeAfterHitToNotLoseItemDrizzly.Value;
                        break;
                    case DifficultyIndex.Normal:
                        _timeForBuff = ArtifactOfDoomConfig.timeAfterHitToNotLoseItemRainstorm.Value;
                        break;
                    case DifficultyIndex.Hard:
                        _timeForBuff = ArtifactOfDoomConfig.timeAfterHitToNotLoseItemMonsoon.Value;
                        break;
                    default:
                        break;
                }
                    
                QueueLostItemSprite = new Dictionary<uint, Queue<ItemDef>>();
                QueueGainedItemSprite = new Dictionary<uint, Queue<ItemDef>>();
                _playerNames = new List<CharacterBody>();
                _counters = new List<double>();
                _lockNetworkUser.Clear();
            };
        }

        private void RegisterRunStartEvent()
        {
            On.RoR2.Run.Start += (orig, self) =>
            {
                orig(self);

                ArtifactOfDoomUI.IsArtifactActive.Invoke(IsActiveAndEnabled(), result =>
                {
                    HandleRpcResult(result);
                });

                ArtifactOfDoomUI.IsCalculationSacrifice.Invoke(ArtifactOfDoomConfig.useArtifactOfSacrificeCalculation.Value, result =>
                {
                    HandleRpcResult(result);
                });
            };
        }

        private void RegisterCharacterBodyOnInventoryChangedEvent()
        {
            On.RoR2.CharacterBody.OnInventoryChanged += (orig, self) =>
            {
                orig(self);
                try
                {
                    if (!IsActiveAndEnabled() || !self.isPlayerControlled)
                    {
                        return;
                    }

                    double enemyCountToTrigger = CalculateEnemyCountToTrigger(self.inventory);
                    AddCharacterBodyToPlayerNamesList(self);

                    NetworkUser networkUser = GetNetworkUserOfCharacterBody(self);
                    if (!_lockNetworkUser.ContainsKey(networkUser))
                    {
                        _lockNetworkUser.Add(networkUser, false);
                    }

                    if (_lockNetworkUser[networkUser] == false)
                    {
                        _lockNetworkUser[networkUser] = true;
                        var request = new UpdateProgressBarRequest(_counters[_playerNames.IndexOf(self)], enemyCountToTrigger);
                        ArtifactOfDoomUI.UpdateProgressBar.Invoke(request, result =>
                        {
                            HandleRpcResult(result);

                            _lockNetworkUser[networkUser] = false;
                        }, networkUser);
                    }
                }
                catch (Exception e)
                {
                    LogErrorMessage($"Error while inventory changed: \n{e}");
                }
            };
        }        

        private void RegisterGlobalEventManagerOnCharacterDeathEvent()
        {
            On.RoR2.GlobalEventManager.OnCharacterDeath += (orig, self, damageReport) =>
            {
                orig(self, damageReport);

                CharacterBody currentBody;
                if (damageReport.attackerOwnerMaster != null)
                {
                    currentBody = damageReport.attackerOwnerMaster.GetBody();
                }
                else
                {
                    currentBody = damageReport.attackerBody;
                }

                if (!IsActiveAndEnabled() || Run.instance.isGameOverServer || damageReport.victimBody.isPlayerControlled || damageReport.attackerBody == null
                    || damageReport.attackerBody.inventory == null || damageReport.victimBody.inventory == null || !currentBody.isPlayerControlled)
                {
                    return;
                }

                if (damageReport.attackerOwnerMaster != null)
                {
                    AddCharacterBodyToPlayerNamesList(damageReport.attackerOwnerMaster.GetBody());
                }
                else
                {
                    AddCharacterBodyToPlayerNamesList(damageReport.attackerBody);
                }

                uint pos = 0;

                double enemyCountToTrigger = CalculateEnemyCountToTrigger(currentBody.inventory);

                // TODO: Would be good to extract this if condition out into it's own local variable/own method with a name that describes what it is actually checking for
                if (_counters[_playerNames.IndexOf(currentBody)] <= enemyCountToTrigger && !ArtifactOfDoomConfig.useArtifactOfSacrificeCalculation.Value)
                {
                    SendUpdateProgressBarRequest(damageReport, currentBody, enemyCountToTrigger);
                }
                else
                {
                    // This was changed to give an item if the Artifact Of Sacrifice is enabled
                    // Before this seemed like it wanted to do that, but was instead giving an item if attackerOwnerMaster got a kill and Sacrifice WOULD NOT have given an item
                    var isUsingArtifactOfSacrifice = damageReport.attackerOwnerMaster != null && ArtifactOfDoomConfig.useArtifactOfSacrificeCalculation.Value && IsItemGivenFromArtifactOfSacrifice(damageReport);

                    double chanceToTrigger = isUsingArtifactOfSacrifice ? GetCharacterSpecificBuffLengthMultiplier(currentBody.baseNameToken) * 100
                                                                        : GetCharacterSpecificItemCount(currentBody.baseNameToken) * 100;
                    var rand = new Random();
                    while (chanceToTrigger > rand.Next(0, 99))
                    {
                        ItemIndex addedItem = GetRandomItem();
                        currentBody.inventory.GiveItem(addedItem);

                        if (ArtifactOfDoomConfig.enableChatItemOutput.Value)
                        {
                            SendItemChatMessage(currentBody, addedItem, ItemChangeAction.Gain);
                        }

                        PlayerStatsComponent.FindBodyStatSheet(currentBody).PushStatValue(_statsGainItems, 1UL);

                        if (!QueueGainedItemSprite.ContainsKey(currentBody.netId.Value))
                        {
                            QueueGainedItemSprite.Add(currentBody.netId.Value, new Queue<ItemDef>());
                        }

                        pos = currentBody.netId.Value;
                        QueueGainedItemSprite[pos].Enqueue(ItemCatalog.GetItemDef(addedItem));
                        chanceToTrigger -= 100;
                    }

                    _counters[_playerNames.IndexOf(currentBody)]++;

                    if (QueueGainedItemSprite[pos].Count > 10)
                    {
                        QueueGainedItemSprite[pos].Dequeue();
                    }

                    SendPlayerItemGainRequest(damageReport, pos, isUsingArtifactOfSacrifice);

                    SendUpdateProgressBarRequest(damageReport, currentBody, chanceToTrigger);

                    _counters[_playerNames.IndexOf(currentBody)] = 0;
                }
            };
        }        

        private void RegisterHealthComponentTakeDamageEvent()
        {
            On.RoR2.HealthComponent.TakeDamage += (orig, self, damageinfo) =>
            {
                //For adding possibility to dont lose items for some time: characterBody.AddTimedBuff(BuffIndex.Immune, duration);
                orig(self, damageinfo);

                if (!IsActiveAndEnabled() || damageinfo.rejected || self.body == null || self.body.inventory == null || Run.instance.isGameOverServer
                    || damageinfo == null || damageinfo.attacker == null || self.body.HasBuff(ArtifactOfDoomConfig.buffIndexDidLoseItem))
                {
                    LogTakeDamageEventWarningMessages(self.body, Run.instance.isGameOverServer, damageinfo);

                    return;
                }

                LogWarningMessage();

                int totalItems = GetTotalItemCountOfPlayer(self.body.inventory);
                if (self.body.isPlayerControlled && (totalItems > 0) && self.name != damageinfo.attacker.name)
                {
                    Dictionary<ItemIndex, int> itemIndexDict = new Dictionary<ItemIndex, int>();
                    List<ItemIndex> itemIndexes = new List<ItemIndex>();

                    // TODO: Is there a way to add to a collection of items on pickup/loss? Would save having to iterate through all item types to find which ones are currently in the inventory
                    foreach (var element in ItemCatalog.allItems)
                    {
                        if (self.body.inventory.GetItemCount(element) > 0)
                        {
                            itemIndexDict.Add(element, self.body.inventory.GetItemCount(element));
                            itemIndexes.Add(element);
                        }
                    }

                    double chanceToTrigger = 100.0;
                    if (totalItems <= (ArtifactOfDoomConfig.minItemsPerStage.Value * _currentStage))
                    {
                        chanceToTrigger = (double)Math.Sqrt(totalItems / (_currentStage * (double)ArtifactOfDoomConfig.minItemsPerStage.Value));
                        chanceToTrigger *= 100;
                    }

                    var rand = new Random();
                    for (int i = 0; i < self.body.inventory.GetItemCount(ItemIndex.Clover) + 1; i++)
                    {
                        int randomValue = rand.Next(1, 100);

                        if (chanceToTrigger < randomValue)
                        {
                            return;
                        }
                    }

                    if (totalItems > (ArtifactOfDoomConfig.maxItemsPerStage.Value * _currentStage))
                    {
                        chanceToTrigger = Math.Pow(totalItems / ((double)ArtifactOfDoomConfig.maxItemsPerStage.Value * _currentStage), ArtifactOfDoomConfig.exponentailFactorToCalculateSumOfLostItems.Value);
                        chanceToTrigger *= 100;
                    }

                    uint pos = 50000;

                    while (chanceToTrigger > 0)
                    {
                        if (!QueueLostItemSprite.ContainsKey(self.body.netId.Value))
                        {
                            QueueLostItemSprite.Add(self.body.netId.Value, new Queue<ItemDef>());
                        }
                        
                        pos = self.body.netId.Value;

                        if (chanceToTrigger < rand.Next(0, 99))
                        {
                            break;
                        }

                        int randomPosition = rand.Next(0, itemIndexDict.Count - 1);
                        ItemIndex itemToRemove = itemIndexes[randomPosition];
                        while ((itemIndexDict[itemToRemove] == 0))
                        {
                            randomPosition = rand.Next(0, itemIndexDict.Count - 1);
                            itemToRemove = itemIndexes[randomPosition];
                        }

                        itemIndexDict[itemToRemove]--;

                        if (!ItemCatalog.lunarItemList.Contains(itemToRemove) && ItemCatalog.GetItemDef(itemToRemove).tier != ItemTier.NoTier && itemToRemove != ItemIndex.CaptainDefenseMatrix)
                        {
                            self.body.inventory.RemoveItem(itemToRemove, 1);

                            if (ArtifactOfDoomConfig.enableChatItemOutput.Value)
                            {
                                SendItemChatMessage(self.body, itemToRemove, ItemChangeAction.Loss);
                            }

                            PlayerStatsComponent.FindBodyStatSheet(self.body).PushStatValue(_statsLostItems, 1UL);

                            QueueLostItemSprite[pos].Enqueue(ItemCatalog.GetItemDef(itemToRemove));
                            if (QueueLostItemSprite[pos].Count > 10)
                            {
                                QueueLostItemSprite[pos].Dequeue();
                            }

                            double buffLengthMultiplier = GetCharacterSpecificBuffLengthMultiplier(self.body.baseNameToken);
                            self.body.AddTimedBuff(ArtifactOfDoomConfig.buffIndexDidLoseItem, (float)(_timeForBuff * (float)buffLengthMultiplier));
                        }

                        chanceToTrigger -= 100;
                    }

                    SendPlayerItemLossRequest(self.body, pos);
                }
            };
        }
        
        private static void LogTakeDamageEventWarningMessages(CharacterBody body, bool isGameOverServer, DamageInfo damageinfo)
        {
            if (body == null)
            {
                LogWarningMessage("body == null)");
            }

            if (body.inventory == null)
            {
                LogWarningMessage("body.inventory == null)");
            }

            if (isGameOverServer)
            {
                LogWarningMessage("RoR2.Run.instance.isGameOverServer)");
            }

            if (damageinfo == null)
            {
                LogWarningMessage("damageinfo == null)");
            }

            if (damageinfo.attacker == null)
            {
                LogWarningMessage("damageinfo.attacker.name==null)");
            }

            if (body.HasBuff(ArtifactOfDoomConfig.buffIndexDidLoseItem))
            {
                LogWarningMessage("you did lose an item not long ago so you don't lose one now");
            }
        }

        private static void LogWarningMessage(string message = "Debug", [CallerLineNumber] int lineNumber = 0)
        {
            if (Debug)
            {
                string warning = $"Line {lineNumber}: {message}";
                UnityEngine.Debug.LogWarning(warning); 
            }
        }

        private static void LogErrorMessage(string message = "Error", [CallerLineNumber] int lineNumber = 0)
        {
            string warning = $"Line {lineNumber}: {message}";
            UnityEngine.Debug.LogWarning(warning);
        }

        private static void LogInfoMessage(string message)
        {
            UnityEngine.Debug.Log(message);
        }

        private static void AddCharacterBodyToPlayerNamesList(CharacterBody self)
        {
            if (!_playerNames.Contains(self))
            {
                _playerNames.Add(self);
                _counters.Add(0d);
            }
        }

        private void HandleRpcResult(RpcResult result)
        {
            if (result.Success)
            {
                HandleSuccessRpcResult(result);
            }
            else
            {
                HandleFailedRpcResult(result);
            }
        }

        private void HandleSuccessRpcResult(RpcResult result)
        {
            switch (result.Severity)
            {
                case LogSeverity.Info:
                    LogInfoMessage(result.Message);
                    break;
                default:
                    break;
            }
        }

        private void HandleFailedRpcResult(RpcResult result)
        {
            switch (result.Severity)
            {
                case LogSeverity.Info:
                    LogInfoMessage(result.Message);
                    break;
                case LogSeverity.Warning:
                    LogWarningMessage(result.Message);
                    break;
                case LogSeverity.Error:
                    LogErrorMessage(result.Message);
                    break;
                case LogSeverity.None:
                default:
                    break;
            }
        }

        private void SendPlayerItemGainRequest(DamageReport damageReport, uint pos, bool isMasterOwner)
        {
            NetworkUser networkUser = GetNetworkUserOfDamageReport(damageReport, isMasterOwner);
            SendUpdatePlayerItemsRequest(networkUser, pos, ItemChangeAction.Gain);
        }

        private void SendPlayerItemLossRequest(CharacterBody body, uint pos)
        {
            NetworkUser networkUser = GetNetworkUserOfCharacterBody(body);
            SendUpdatePlayerItemsRequest(networkUser, pos, ItemChangeAction.Loss);
        }

        private void SendUpdatePlayerItemsRequest(NetworkUser networkUser, uint pos, ItemChangeAction itemChangeAction)
        {
            if (!_lockItemGainNetworkUser.ContainsKey(networkUser))
            {
                _lockItemGainNetworkUser.Add(networkUser, false);
            }

            _lockItemGainNetworkUser[networkUser] = true;
            UpdatePlayerItemsRequest itemChangeRequest = GenerateUpdatePlayerItemsRequest(pos, itemChangeAction);
            ArtifactOfDoomUI.UpdatePlayerItems.Invoke(itemChangeRequest, result =>
            {
                HandleRpcResult(result);

                _lockItemGainNetworkUser[networkUser] = false;
            }, networkUser);
        }

        private static UpdatePlayerItemsRequest GenerateUpdatePlayerItemsRequest(uint pos, ItemChangeAction itemChangeAction)
        {
            var itemChangeRequest = new UpdatePlayerItemsRequest(itemChangeAction);

            if (itemChangeAction == ItemChangeAction.Gain)
            {
                foreach (var element in QueueGainedItemSprite[pos])
                {
                    itemChangeRequest.AddItem(element.name);
                }
            }
            else if (itemChangeAction == ItemChangeAction.Loss)
            {
                foreach (var element in QueueLostItemSprite[pos])
                {
                    itemChangeRequest.AddItem(element.name);
                }
            }

            return itemChangeRequest;
        }

        private static void SendItemChatMessage(CharacterBody currentBody, ItemIndex addedItem, ItemChangeAction itemChangeAction)
        {
            var pickupDef = ItemCatalog.GetItemDef(addedItem);
            var pickupName = Language.GetString(pickupDef.nameToken);
            var playerColor = currentBody.GetColoredUserName();
            var itemCount = currentBody.inventory.GetItemCount(pickupDef.itemIndex);
            var itemAction = itemChangeAction == ItemChangeAction.Gain ? "gained" : "lost";
            Chat.SendBroadcastChat(new Chat.SimpleChatMessage
            {
                baseToken =
                playerColor + $"<color=#{_grayColor}> {itemAction}</color> " +
                $"{pickupName ?? "???"} ({itemCount})</color> <color=#{_grayColor}></color>"
            });
        }

        private void SendUpdateProgressBarRequest(DamageReport damageReport, CharacterBody currentBody, double enemyCountToTrigger)
        {
            _counters[_playerNames.IndexOf(currentBody)]++;

            NetworkUser networkUser = GetNetworkUserOfDamageReport(damageReport, true);
            if (!_lockNetworkUser.ContainsKey(networkUser))
            {
                _lockNetworkUser.Add(networkUser, false);
            }

            if (_lockNetworkUser[networkUser] == false)
            {
                _lockNetworkUser[networkUser] = true;
                var request = new UpdateProgressBarRequest(_counters[_playerNames.IndexOf(currentBody)], enemyCountToTrigger);
                ArtifactOfDoomUI.UpdateProgressBar.Invoke(request, result =>
                {
                    HandleRpcResult(result);

                    _lockNetworkUser[networkUser] = false;
                }, networkUser);
            }
        }

        private NetworkUser GetNetworkUserOfDamageReport(DamageReport report, bool withMaster)
        {
            NetworkUser tempNetworkUser = null;
            foreach (var element in NetworkUser.readOnlyInstancesList)
            {
                if (report.attackerOwnerMaster != null && withMaster)
                {
                    if (element.GetCurrentBody() != null)
                    {
                        if (element.GetCurrentBody().netId == report.attackerOwnerMaster.GetBody().netId)
                        {
                            tempNetworkUser = element;
                        }
                    }
                }
                else
                {
                    if (element.GetCurrentBody() != null)
                    {
                        if (element.GetCurrentBody().netId == report.attackerBody.netId)
                        {
                            tempNetworkUser = element;
                        }
                    }
                }
            }
            return tempNetworkUser;
        }

        private NetworkUser GetNetworkUserOfCharacterBody(CharacterBody body)
        {
            NetworkUser tempNetworkUser = null;
            foreach (var element in NetworkUser.readOnlyInstancesList)
            {
                if (element.GetCurrentBody() != null)
                {
                    if (element.GetCurrentBody().netId == body.netId)
                        tempNetworkUser = element;
                }
            }
            return tempNetworkUser;
        }

        private int GetTotalItemCountOfPlayer(Inventory inventory)
        {
            return inventory.GetTotalItemCountOfTier(ItemTier.Tier1) +
            inventory.GetTotalItemCountOfTier(ItemTier.Tier2) +
            inventory.GetTotalItemCountOfTier(ItemTier.Tier3);
        }

        private double CalculateEnemyCountToTrigger(Inventory inventory)
        {
            var totalItems = GetTotalItemCountOfPlayer(inventory);
            var calculatedValue = totalItems - _currentStage * ArtifactOfDoomConfig.averageItemsPerStage.Value;
            double calculatedEnemyCountToTrigger;
            if (calculatedValue >= 0)
            {
                calculatedEnemyCountToTrigger = Math.Pow(calculatedValue, ArtifactOfDoomConfig.exponentTriggerItems.Value);
            }
            else
            {
                calculatedEnemyCountToTrigger = Math.Pow(totalItems, ArtifactOfDoomConfig.exponentailFactorIfYouAreUnderAverageItemsPerStage.Value);
            }

            if (calculatedEnemyCountToTrigger < 1)
            {
                calculatedEnemyCountToTrigger = 1;
            }

            if (RunArtifactManager.instance.IsArtifactEnabled(RoR2Content.Artifacts.swarmsArtifactDef) && ArtifactOfDoomConfig.artifactOfSwarmNerf.Value)
            {
                calculatedEnemyCountToTrigger *= 2;
            }

            return calculatedEnemyCountToTrigger + 2;
        }

        private double GetCharacterSpecificItemCount(string baseNameToken)
        {
            switch (baseNameToken)
            {
                case "COMMANDO_BODY_NAME":
                    LogWarningMessage($"Character baseNameToken = {baseNameToken} returning: Commando"); 
                    return ArtifactOfDoomConfig.CommandoBonusItems.Value;
                case "HUNTRESS_BODY_NAME":
                    LogWarningMessage($"Character baseNameToken = {baseNameToken} returning: Huntress"); 
                    return ArtifactOfDoomConfig.HuntressBonusItems.Value;
                case "ENGI_BODY_NAME":
                    LogWarningMessage($"Character baseNameToken = {baseNameToken} returning: Engineer"); 
                    return ArtifactOfDoomConfig.EngineerBonusItems.Value;
                case "TOOLBOT_BODY_NAME":
                    LogWarningMessage($"Character baseNameToken = {baseNameToken} returning: MULT"); 
                    return ArtifactOfDoomConfig.MULTBonusItems.Value;
                case "MAGE_BODY_NAME":
                    LogWarningMessage($"Character baseNameToken = {baseNameToken} returning: Artificer"); 
                    return ArtifactOfDoomConfig.ArtificerBonusItems.Value;
                case "MERC_BODY_NAME":
                    LogWarningMessage($"Character baseNameToken = {baseNameToken} returning: Mercenary"); 
                    return ArtifactOfDoomConfig.MercenaryBonusItems.Value;
                case "TREEBOT_BODY_NAME":
                    LogWarningMessage($"Character baseNameToken = {baseNameToken} returning: Rex"); 
                    return ArtifactOfDoomConfig.RexBonusItems.Value;
                case "LOADER_BODY_NAME":
                    LogWarningMessage($"Character baseNameToken = {baseNameToken} returning: Loader"); 
                    return ArtifactOfDoomConfig.LoaderBonusItems.Value;
                case "CROCO_BODY_NAME":
                    LogWarningMessage($"Character baseNameToken = {baseNameToken} returning: Acrid"); 
                    return ArtifactOfDoomConfig.AcridBonusItems.Value;
                case "CAPTAIN_BODY_NAME":
                    LogWarningMessage($"Character baseNameToken = {baseNameToken} returning: Captain"); 
                    return ArtifactOfDoomConfig.CaptainBonusItems.Value;
                default:
                    string CustomChars = ArtifactOfDoomConfig.CustomChars.Value;
                    List<Character> characters=CustomChars.FromJson<List<Character>>();
                    foreach(var element in characters)
                    {
                        if (baseNameToken == element.Name)
                        {
                            LogWarningMessage($"Character baseNameToken = {baseNameToken} returning: CustomChar MultiplierForTimedBuff");
                            return element.BonusItems;
                        }
                    }
                    UnityEngine.Debug.LogWarning("did not find a valid configuation setting for Character " + baseNameToken + " you can add one in the settings");
                    LogWarningMessage($"Character baseNameToken = {baseNameToken} returning: CustomSurvivorBonusItems");
                    return ArtifactOfDoomConfig.CustomSurvivorBonusItems.Value;
            }
        }

        private double GetCharacterSpecificBuffLengthMultiplier(string baseNameToken)
        {
            switch (baseNameToken)
            {
                case "COMMANDO_BODY_NAME":
                    LogWarningMessage($"Character baseNameToken = {baseNameToken} returning: Commando");
                    return ArtifactOfDoomConfig.CommandoMultiplierForTimedBuff.Value;
                case "HUNTRESS_BODY_NAME":
                    LogWarningMessage($"Character baseNameToken = {baseNameToken} returning: Huntress");
                    return ArtifactOfDoomConfig.HuntressMultiplierForTimedBuff.Value;
                case "ENGI_BODY_NAME":
                    LogWarningMessage($"Character baseNameToken = {baseNameToken} returning: Engineer");
                    return ArtifactOfDoomConfig.EngineerMultiplierForTimedBuff.Value;
                case "TOOLBOT_BODY_NAME":
                    LogWarningMessage($"Character baseNameToken = {baseNameToken} returning: MULT");
                    return ArtifactOfDoomConfig.MULTMultiplierForTimedBuff.Value;
                case "MAGE_BODY_NAME":
                    LogWarningMessage($"Character baseNameToken = {baseNameToken} returning: Artificer");
                    return ArtifactOfDoomConfig.ArtificerMultiplierForTimedBuff.Value;
                case "MERC_BODY_NAME":
                    LogWarningMessage($"Character baseNameToken = {baseNameToken} returning: Mercenary");
                    return ArtifactOfDoomConfig.MercenaryMultiplierForTimedBuff.Value;
                case "TREEBOT_BODY_NAME":
                    LogWarningMessage($"Character baseNameToken = {baseNameToken} returning: Rex");
                    return ArtifactOfDoomConfig.RexMultiplierForTimedBuff.Value;
                case "LOADER_BODY_NAME":
                    LogWarningMessage($"Character baseNameToken = {baseNameToken} returning: Loader");
                    return ArtifactOfDoomConfig.LoaderMultiplierForTimedBuff.Value;
                case "CROCO_BODY_NAME":
                    LogWarningMessage($"Character baseNameToken = {baseNameToken} returning: Acrid");
                    return ArtifactOfDoomConfig.AcridMultiplierForTimedBuff.Value;
                case "CAPTAIN_BODY_NAME":
                    LogWarningMessage($"Character baseNameToken = {baseNameToken} returning: Captain");
                    return ArtifactOfDoomConfig.CaptainMultiplierForTimedBuff.Value;
                default:
                    string CustomChars = ArtifactOfDoomConfig.CustomChars.Value;
                    List<Character> characters=CustomChars.FromJson<List<Character>>();
                    foreach(var element in characters)
                    {
                        if (baseNameToken == element.Name)
                        {
                            LogWarningMessage($"Character baseNameToken = {baseNameToken} returning: CustomChar MultiplierForTimedBuff");
                            return element.MultiplierForTimedBuff;
                        }
                    }
                    UnityEngine.Debug.LogWarning("did not find a valid configuation setting for Character " + baseNameToken + " you can add one in the settings");
                    LogWarningMessage($"Character baseNameToken = {baseNameToken} returning: CustomSurvivorMultiplierForTimedBuff");
                    return ArtifactOfDoomConfig.CustomSurvivorMultiplierForTimedBuff.Value;
            }
        }        

        public ItemIndex GetRandomItem()
        {
            var tier1 = ItemDropAPI.GetDefaultDropList(ItemTier.Tier1);
            var tier2 = ItemDropAPI.GetDefaultDropList(ItemTier.Tier2);
            var tier3 = ItemDropAPI.GetDefaultDropList(ItemTier.Tier3);

            WeightedSelection<List<ItemIndex>> weightedSelection = new WeightedSelection<List<ItemIndex>>();
            weightedSelection.AddChoice(tier1, 80f);
            weightedSelection.AddChoice(tier2, 19f);
            weightedSelection.AddChoice(tier3, 1f);

            List<ItemIndex> selectedList = weightedSelection.Evaluate(UnityEngine.Random.value);

            var givenItem = selectedList[UnityEngine.Random.Range(0, selectedList.Count)];
            return givenItem;
        }

        protected override void UnloadBehavior()
        {
            QueueLostItemSprite = new Dictionary<uint, Queue<ItemDef>>();
            QueueGainedItemSprite = new Dictionary<uint, Queue<ItemDef>>();
            _statsLostItems = null;
            _statsGainItems = null;
            _playerNames = new List<CharacterBody>();
            _counters = new List<double>();
        }

        private bool IsItemGivenFromArtifactOfSacrifice(DamageReport damageReport)
        {
            if (!ArtifactOfDoomConfig.useArtifactOfSacrificeCalculation.Value || !damageReport.victimMaster 
                || (damageReport.attackerTeamIndex == damageReport.victimTeamIndex && damageReport.victimMaster.minionOwnership.ownerMaster))
            {
                return false;
            }
            
            float expAdjustedDropChancePercent = Util.GetExpAdjustedDropChancePercent(_baseDropChance, damageReport.victim.gameObject);
            
            if (Util.CheckRoll(expAdjustedDropChancePercent, 0f, null))
            {
                return true;
            }
            return false;
        }
    }
}