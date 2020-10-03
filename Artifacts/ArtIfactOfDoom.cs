
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
        private static List<CharacterBody> _playerNames = new List<CharacterBody>();
        private static List<int> _counters = new List<int>();
        private int _currentStage = 0;

        private Dictionary<NetworkUser, bool> _lockNetworkUser = new Dictionary<NetworkUser, bool>();
        private Dictionary<NetworkUser, bool> _lockItemGainNetworkUser = new Dictionary<NetworkUser, bool>();

        private static StatDef _statsLostItems;
        private static StatDef _statsGainItems;
        
        public static Dictionary<uint, Queue<ItemDef>> QueueLostItemSprite = new Dictionary<uint, Queue<ItemDef>>();
        public static Dictionary<uint, Queue<ItemDef>> QueueGainedItemSprite = new Dictionary<uint, Queue<ItemDef>>();

        private static double _timeForBuff = 0.0;

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
            _counters = new List<int>();
            _currentStage = 0;

            _statsLostItems = null;
            _statsGainItems = null;

            _statsLostItems = StatDef.Register("Lostitems", StatRecordType.Sum, StatDataType.ULong, 0, null);
            _statsGainItems = StatDef.Register("Gainitems", StatRecordType.Sum, StatDataType.ULong, 0, null);

            // TODO: These register methods can be moved into a RegisterEvents method
            RegisterGameEndReportPanelControllerAwakeEvent();
            RegisterSceneDirectorPopulateSceneEvent();
            RegisterRunStartEvent();
            RegisterCharacterBodyOnInventoryChangedEvent();
            RegisterGlobalEventManagerOnCharacterDeathEvent();
            RegisterHealthComponentTakeDamageEvent();
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

                int totalItems = getTotalItemCountOfPlayer(self.body.inventory);
                if (self.body.isPlayerControlled && (totalItems > 0) && self.name != damageinfo.attacker.name)
                {
                    Dictionary<ItemIndex, int> itemIndexDict = new Dictionary<ItemIndex, int>();
                    List<ItemIndex> itemIndexes = new List<ItemIndex>();
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

                    int lostItems = 0;

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

                        lostItems++;
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

                            // TODO: Move this duplicated logic into own method
                            if (ArtifactOfDoomConfig.enableChatItemOutput.Value)
                            {
                                var pickupDef = ItemCatalog.GetItemDef(itemToRemove);
                                var pickupName = Language.GetString(pickupDef.nameToken);
                                var playerColor = self.body.GetColoredUserName();
                                var itemCount = self.body.inventory.GetItemCount(pickupDef.itemIndex);
                                Chat.SendBroadcastChat(new Chat.SimpleChatMessage
                                {
                                    baseToken =
                                    self.body.GetColoredUserName() + $"<color=#{_grayColor}> lost</color> " +
                                    $"{pickupName ?? "???"} ({itemCount})</color> <color=#{_grayColor}></color>"
                                });
                            }

                            PlayerStatsComponent.FindBodyStatSheet(self.body).PushStatValue(_statsLostItems, 1UL);

                            QueueLostItemSprite[pos].Enqueue(ItemCatalog.GetItemDef(itemToRemove));
                            if (QueueLostItemSprite[pos].Count > 10)
                            {
                                QueueLostItemSprite[pos].Dequeue();
                            }

                            double buffLengthMultiplier = getCharacterSpecificBuffLengthMultiplier(self.body.baseNameToken);
                            self.body.AddTimedBuff(ArtifactOfDoomConfig.buffIndexDidLoseItem, (float)(_timeForBuff * (float)buffLengthMultiplier));
                        }

                        chanceToTrigger -= 100;
                    }

                    NetworkUser tempNetworkUser = getNetworkUserOfCharacterBody(self.body);

                    if (tempNetworkUser == null)
                    {
                        LogErrorMessage("--------------------------------tempNetworkUser(lostitems)==null---------------------------");
                    }

                    if (!_lockNetworkUser.ContainsKey(tempNetworkUser))
                    {
                        _lockNetworkUser.Add(tempNetworkUser, false);
                    }

                    //TODO: Use class insead of temp string
                    string temp = "";
                    foreach (var element in QueueLostItemSprite[pos])
                    {
                        temp += element.name + " ";
                    }
                    
                    if (_lockNetworkUser[tempNetworkUser] == false)
                    {
                        _lockNetworkUser[tempNetworkUser] = true;
                        ArtifactOfDoomUI.AddLostItemsOfPlayers.Invoke(temp, result =>
                        {
                            _lockNetworkUser[tempNetworkUser] = false;
                        }, tempNetworkUser);

                        //TODO: Use class insead of temp string
                        int enemyCountToTrigger = calculateEnemyCountToTrigger(self.body.inventory);
                        string tempString = _counters[_playerNames.IndexOf(self.body)] + "," + enemyCountToTrigger;
                        ArtifactOfDoomUI.UpdateProgressBar.Invoke(tempString, result =>
                        {
                        }, tempNetworkUser);
                    }
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

                // TODO: Check if this logic is duplicated elsewhere, refactor into own method if so
                if (damageReport.attackerOwnerMaster != null)
                {
                    if (!_playerNames.Contains(damageReport.attackerBody))
                    {
                        _playerNames.Add(damageReport.attackerOwnerMaster.GetBody());
                        _counters.Add(0);
                    }
                }

                // TODO: Think this logic is duplicated elsewhere, refactor into own method
                if (!_playerNames.Contains(damageReport.attackerBody))
                {
                    _playerNames.Add(damageReport.attackerBody);
                    _counters.Add(0);
                }

                uint pos = 0;

                int enemyCountToTrigger = calculateEnemyCountToTrigger(currentBody.inventory);

                // TODO: This method should be renamed to represent what it actually returns, potentially need to rename variable too
                bool enemyTrigger = getEnemyDropRate(damageReport);

                // TODO: Would be good to extract this if condition out into it's own local variable/own method with a name that describes what it is actually checking for
                if (_counters[_playerNames.IndexOf(currentBody)] <= enemyCountToTrigger && !ArtifactOfDoomConfig.useArtifactOfSacrificeCalculation.Value)
                {
                    _counters[_playerNames.IndexOf(currentBody)]++;

                    NetworkUser tempNetworkUser = getNetworkUserOfDamageReport(damageReport, true);
                    // TODO: Use class instead of temp string here
                    string temp = _counters[_playerNames.IndexOf(currentBody)] + "," + enemyCountToTrigger;
                    ArtifactOfDoomUI.UpdateProgressBar.Invoke(temp, result =>
                    {
                    }, tempNetworkUser);

                }
                // TODO: The below else statement contains a nested if/else statement with a lot of logic in both parts, this should be refactored out into different methods at least
                else
                {
                    CharacterBody body;

                    if (damageReport.attackerOwnerMaster != null && ArtifactOfDoomConfig.useArtifactOfSacrificeCalculation.Value && !enemyTrigger)
                    {
                        body = damageReport.attackerOwnerMaster.GetBody();

                        // TODO: Instead of multiplyinng by 100 here, just get this method to multiply any calculated value by 100 before returning the result
                        double chanceToTrigger = getCharacterSpecificBuffLengthMultiplier(body.baseNameToken) * 100;
                        var rand = new Random();
                        while (chanceToTrigger > rand.Next(0, 99))
                        {
                            ItemIndex addedItem = GetRandomItem();
                            body.inventory.GiveItem(addedItem);

                            if (ArtifactOfDoomConfig.enableChatItemOutput.Value)
                            {
                                var pickupDef = ItemCatalog.GetItemDef(addedItem);
                                var pickupName = Language.GetString(pickupDef.nameToken);
                                var playerColor = damageReport.attackerOwnerMaster.GetBody().GetColoredUserName();
                                var itemCount = damageReport.attackerOwnerMaster.GetBody().inventory.GetItemCount(pickupDef.itemIndex);
                                Chat.SendBroadcastChat(new Chat.SimpleChatMessage
                                {
                                    baseToken =
                                    damageReport.attackerOwnerMaster.GetBody().GetColoredUserName() + $"<color=#{_grayColor}> gained</color> " +
                                    $"{pickupName ?? "???"} ({itemCount})</color> <color=#{_grayColor}></color>"
                                });
                            }

                            PlayerStatsComponent.FindBodyStatSheet(body).PushStatValue(_statsGainItems, 1UL);
                            if (!QueueGainedItemSprite.ContainsKey(body.netId.Value))
                            {
                                QueueGainedItemSprite.Add(body.netId.Value, new Queue<ItemDef>());
                            }
                            pos = body.netId.Value;
                            QueueGainedItemSprite[pos].Enqueue(ItemCatalog.GetItemDef(addedItem));
                            chanceToTrigger -= 100;
                        }
                    }
                    else
                    {
                        // TODO: The only difference between the above if block code and this is the body object. This whole if else statement can be removed and the logic refactored into it's own method.
                        body = damageReport.attackerBody;
                        double chanceToTrigger = getCharacterSpecificItemCount(body.baseNameToken) * 100;
                        var rand = new Random();
                        while (chanceToTrigger > rand.Next(0, 99))
                        {
                            ItemIndex addedItem = GetRandomItem();
                            body.inventory.GiveItem(addedItem);

                            if (ArtifactOfDoomConfig.enableChatItemOutput.Value)
                            {
                                var pickupDef = ItemCatalog.GetItemDef(addedItem);
                                var pickupName = Language.GetString(pickupDef.nameToken);
                                var playerColor = body.GetColoredUserName();
                                var itemCount = body.inventory.GetItemCount(pickupDef.itemIndex);
                                Chat.SendBroadcastChat(new Chat.SimpleChatMessage
                                {
                                    baseToken =
                                    body.GetColoredUserName() + $"<color=#{_grayColor}> gained</color> " +
                                    $"{pickupName ?? "???"} ({itemCount})</color> <color=#{_grayColor}></color>"
                                });
                            }
                            PlayerStatsComponent.FindBodyStatSheet(body).PushStatValue(_statsGainItems, 1UL);
                            if (!QueueGainedItemSprite.ContainsKey(body.netId.Value))
                            {
                                QueueGainedItemSprite.Add(body.netId.Value, new Queue<ItemDef>());
                            }
                            pos = body.netId.Value;
                            QueueGainedItemSprite[pos].Enqueue(ItemCatalog.GetItemDef(addedItem));
                            chanceToTrigger -= 100;
                        }
                    }

                    if (QueueGainedItemSprite[pos].Count > 10)
                    {
                        QueueGainedItemSprite[pos].Dequeue();
                    }

                    NetworkUser tempNetworkUser = getNetworkUserOfDamageReport(damageReport, true);

                    if (!_lockItemGainNetworkUser.ContainsKey(tempNetworkUser))
                    {
                        _lockItemGainNetworkUser.Add(tempNetworkUser, false);
                    }

                    _counters[_playerNames.IndexOf(currentBody)]++;

                    _lockItemGainNetworkUser[tempNetworkUser] = true;

                    // TODO: Use a class instead of a temp string
                    string temp = "";
                    foreach (var element in QueueGainedItemSprite[pos])
                    {
                        temp += element.name + " ";
                    }
                    ArtifactOfDoomUI.AddGainedItemsToPlayers.Invoke(temp, result =>
                    {
                        _lockItemGainNetworkUser[tempNetworkUser] = false;
                    }, tempNetworkUser);

                    // TODO: Use a class instead of a temp string
                    string tempString = _counters[_playerNames.IndexOf(currentBody)] + "," + enemyCountToTrigger;
                    ArtifactOfDoomUI.UpdateProgressBar.Invoke(tempString, result =>
                    {
                    }, tempNetworkUser);

                    _counters[_playerNames.IndexOf(currentBody)] = 0;
                }
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

                    NetworkUser tempNetworkUser = getNetworkUserOfCharacterBody(self);
                    int enemyCountToTrigger = calculateEnemyCountToTrigger(self.inventory);
                    if (!_playerNames.Contains(self))
                    {
                        _playerNames.Add(self);
                        _counters.Add(0);
                    }

                    if (tempNetworkUser != null)
                    {
                        // TODO: Instead of using a temp string here, can make a class with members for the counter index value and the enemyCountToTrigger value
                        string tempString = _counters[_playerNames.IndexOf(self)] + "," + enemyCountToTrigger;
                        ArtifactOfDoomUI.UpdateProgressBar.Invoke(tempString, result =>
                        {
                        }, tempNetworkUser);
                    }
                }
                catch (Exception e)
                {
                    LogErrorMessage($"Error while inventory changed: \n{e}");
                }
            };
        }

        private void RegisterRunStartEvent()
        {
            On.RoR2.Run.Start += (orig, self) =>
            {
                orig(self);
                ArtifactOfDoomUI.IsArtifactActive.Invoke(IsActiveAndEnabled(), result =>
                {
                }, null);
                ArtifactOfDoomUI.IsCalculationSacrifice.Invoke(ArtifactOfDoomConfig.useArtifactOfSacrificeCalculation.Value, result =>
                {
                }, null);
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
                _counters = new List<int>();
                _lockNetworkUser.Clear();
            };
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

        // TODO: Investigate refactoring this method
        private NetworkUser getNetworkUserOfDamageReport(DamageReport report, bool withMaster)
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

        // TODO: Investigate refactoring this method
        private NetworkUser getNetworkUserOfCharacterBody(CharacterBody body)
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

        // TODO: Investigate refactoring this method
        private int getTotalItemCountOfPlayer(Inventory inventory)
        {
            return inventory.GetTotalItemCountOfTier(ItemTier.Tier1) +
            inventory.GetTotalItemCountOfTier(ItemTier.Tier2) +
            inventory.GetTotalItemCountOfTier(ItemTier.Tier3);
        }

        // TODO: Investigate refactoring this method
        private int calculateEnemyCountToTrigger(Inventory inventory)
        {
            var totalItems = getTotalItemCountOfPlayer(inventory);
            var calculatedValue = totalItems - _currentStage * ArtifactOfDoomConfig.averageItemsPerStage.Value;
            int calculatesEnemyCountToTrigger = 0;
            if (calculatedValue >= 0)
                calculatesEnemyCountToTrigger = (int)Math.Pow(calculatedValue, ArtifactOfDoomConfig.exponentTriggerItems.Value);
            else
                calculatesEnemyCountToTrigger = (int)Math.Pow(totalItems, ArtifactOfDoomConfig.exponentailFactorIfYouAreUnderAverageItemsPerStage.Value);
            //calculatesEnemyCountToTrigger =1;

            if (calculatesEnemyCountToTrigger < 1)
                calculatesEnemyCountToTrigger = 1;

            if (RunArtifactManager.instance.IsArtifactEnabled(RoR2Content.Artifacts.swarmsArtifactDef) && ArtifactOfDoomConfig.artifactOfSwarmNerf.Value)
                calculatesEnemyCountToTrigger *= 2;
            return calculatesEnemyCountToTrigger;
        }

        // TODO: Investigate refactoring this method
        private double getCharacterSpecificItemCount(string baseNameToken)
        {
            switch (baseNameToken)
            {
                case "COMMANDO_BODY_NAME":
                    if (Debug) { UnityEngine.Debug.LogWarning($"Character baseNameToken = {baseNameToken} returning: Commando"); }
                    return ArtifactOfDoomConfig.CommandoBonusItems.Value;
                case "HUNTRESS_BODY_NAME":
                    if (Debug) { UnityEngine.Debug.LogWarning($"Character baseNameToken = {baseNameToken} returning: Huntress"); }
                    return ArtifactOfDoomConfig.HuntressBonusItems.Value;
                case "ENGI_BODY_NAME":
                    if (Debug) { UnityEngine.Debug.LogWarning($"Character baseNameToken = {baseNameToken} returning: Engineer"); }
                    return ArtifactOfDoomConfig.EngineerBonusItems.Value;
                case "TOOLBOT_BODY_NAME":
                    if (Debug) { UnityEngine.Debug.LogWarning($"Character baseNameToken = {baseNameToken} returning: MULT"); }
                    return ArtifactOfDoomConfig.MULTBonusItems.Value;
                case "MAGE_BODY_NAME":
                    if (Debug) { UnityEngine.Debug.LogWarning($"Character baseNameToken = {baseNameToken} returning: Artificer"); }
                    return ArtifactOfDoomConfig.ArtificerBonusItems.Value;
                case "MERC_BODY_NAME":
                    if (Debug) { UnityEngine.Debug.LogWarning($"Character baseNameToken = {baseNameToken} returning: Mercenary"); }
                    return ArtifactOfDoomConfig.MercenaryBonusItems.Value;
                case "TREEBOT_BODY_NAME":
                    if (Debug) { UnityEngine.Debug.LogWarning($"Character baseNameToken = {baseNameToken} returning: Rex"); }
                    return ArtifactOfDoomConfig.RexBonusItems.Value;
                case "LOADER_BODY_NAME":
                    if (Debug) { UnityEngine.Debug.LogWarning($"Character baseNameToken = {baseNameToken} returning: Loader"); }
                    return ArtifactOfDoomConfig.LoaderBonusItems.Value;
                case "CROCO_BODY_NAME":
                    if (Debug) { UnityEngine.Debug.LogWarning($"Character baseNameToken = {baseNameToken} returning: Acrid"); }
                    return ArtifactOfDoomConfig.AcridBonusItems.Value;
                case "CAPTAIN_BODY_NAME":
                    if (Debug) { UnityEngine.Debug.LogWarning($"Character baseNameToken = {baseNameToken} returning: Acrid"); }
                    return ArtifactOfDoomConfig.CaptainBonusItems.Value;
                default:
                    string CustomChars = ArtifactOfDoomConfig.CustomChars.Value;

                    //Character characters = TinyJson.JSONParser.FromJson<Character>(CustomChars);
                    List<Character> characters=CustomChars.FromJson<List<Character>>();
                    foreach(var element in characters)
                    {
                        if(baseNameToken == element.Name)
                        return element.BonusItems;
                    }
                    UnityEngine.Debug.LogWarning("did not find a valid configuation setting for Character " + baseNameToken + " you can add one in the settings");
                    if (Debug) { UnityEngine.Debug.LogWarning($"Character baseNameToken = {baseNameToken} returning: Acrid"); }
                    return ArtifactOfDoomConfig.CustomSurvivorBonusItems.Value;
            }
        }

        // TODO: Investigate refactoring this method
        private double getCharacterSpecificBuffLengthMultiplier(string baseNameToken)
        {
            switch (baseNameToken)
            {
                case "COMMANDO_BODY_NAME":
                    if (Debug) { UnityEngine.Debug.LogWarning($"Character baseNameToken = {baseNameToken} returning: Commando"); }
                    return ArtifactOfDoomConfig.CommandoMultiplierForTimedBuff.Value;
                case "HUNTRESS_BODY_NAME":
                    if (Debug) { UnityEngine.Debug.LogWarning($"Character baseNameToken = {baseNameToken} returning: Huntress"); }
                    return ArtifactOfDoomConfig.HuntressMultiplierForTimedBuff.Value;
                case "ENGI_BODY_NAME":
                    if (Debug) { UnityEngine.Debug.LogWarning($"Character baseNameToken = {baseNameToken} returning: Engineer"); }
                    return ArtifactOfDoomConfig.EngineerMultiplierForTimedBuff.Value;
                case "TOOLBOT_BODY_NAME":
                    if (Debug) { UnityEngine.Debug.LogWarning($"Character baseNameToken = {baseNameToken} returning: MULT"); }
                    return ArtifactOfDoomConfig.MULTMultiplierForTimedBuff.Value;
                case "MAGE_BODY_NAME":
                    if (Debug) { UnityEngine.Debug.LogWarning($"Character baseNameToken = {baseNameToken} returning: Artificer"); }
                    return ArtifactOfDoomConfig.ArtificerMultiplierForTimedBuff.Value;
                case "MERC_BODY_NAME":
                    if (Debug) { UnityEngine.Debug.LogWarning($"Character baseNameToken = {baseNameToken} returning: Mercenary"); }
                    return ArtifactOfDoomConfig.MercenaryMultiplierForTimedBuff.Value;
                case "TREEBOT_BODY_NAME":
                    if (Debug) { UnityEngine.Debug.LogWarning($"Character baseNameToken = {baseNameToken} returning: Rex"); }
                    return ArtifactOfDoomConfig.RexMultiplierForTimedBuff.Value;
                case "LOADER_BODY_NAME":
                    if (Debug) { UnityEngine.Debug.LogWarning($"Character baseNameToken = {baseNameToken} returning: Loader"); }
                    return ArtifactOfDoomConfig.LoaderMultiplierForTimedBuff.Value;
                case "CROCO_BODY_NAME":
                    if (Debug) { UnityEngine.Debug.LogWarning($"Character baseNameToken = {baseNameToken} returning: Acrid"); }
                    return ArtifactOfDoomConfig.AcridMultiplierForTimedBuff.Value;
                case "CAPTAIN_BODY_NAME":
                    if (Debug) { UnityEngine.Debug.LogWarning($"Character baseNameToken = {baseNameToken} returning: Acrid"); }
                    return ArtifactOfDoomConfig.CaptainMultiplierForTimedBuff.Value;
                default:
                    string CustomChars = ArtifactOfDoomConfig.CustomChars.Value;

                    //Character characters = TinyJson.JSONParser.FromJson<Character>(CustomChars);
                    List<Character> characters=CustomChars.FromJson<List<Character>>();
                    foreach(var element in characters)
                    {
                        if(baseNameToken == element.Name)
                        return element.MultiplierForTimedBuff;
                    }
                    UnityEngine.Debug.LogWarning("did not find a valid configuation setting for Character " + baseNameToken + " you can add one in the settings");
                    if (Debug) { UnityEngine.Debug.LogWarning($"Character baseNameToken = {baseNameToken} returning: Acrid"); }
                    return ArtifactOfDoomConfig.CustomSurvivorMultiplierForTimedBuff.Value;
            }
        }

        public class Character
        {
            public string Name { get; set; }
            public float MultiplierForTimedBuff { get; set; }
            public float BonusItems { get; set; }
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
            _counters = new List<int>();
        }

        // TODO: Refactor this method - name at least
        private bool getEnemyDropRate(DamageReport damageReport)
        {
            if (!ArtifactOfDoomConfig.useArtifactOfSacrificeCalculation.Value)
                return false;
            if (!damageReport.victimMaster)
            {
                return false;
            }
            if (damageReport.attackerTeamIndex == damageReport.victimTeamIndex && damageReport.victimMaster.minionOwnership.ownerMaster)
            {
                return false;
            }
            float expAdjustedDropChancePercent = Util.GetExpAdjustedDropChancePercent(5f * (float)ArtifactOfDoomConfig.multiplayerForArtifactOfSacrificeDropRate.Value, damageReport.victim.gameObject);
            //Debug.LogFormat("Drop chance from {0}: {1}", new object[]
            //{
            //	damageReport.victimBody,
            //	expAdjustedDropChancePercent
            //});
            if (Util.CheckRoll(expAdjustedDropChancePercent, 0f, null))
            {
                return true;
            }
            return false;
        }
    }
}