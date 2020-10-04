using ArtifactOfDoom.Artifacts.Contracts;
using ArtifactOfDoom.Ui.Contracts;
using BepInEx;
using MiniRpcLib;
using MiniRpcLib.Func;
using RoR2;
using RoR2.UI;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;



#region TODO:
/*

*/
#endregion

namespace ArtifactOfDoom
{
    [R2API.Utils.R2APISubmoduleDependency("ResourcesAPI")]
    [BepInPlugin("com.ohway.UIMod", "UI Modifier", "1.0")]
    [BepInDependency(MiniRpcPlugin.Dependency)]
    public class ArtifactOfDoomUI : BaseUnityPlugin
    {
        public GameObject ModCanvas = null;
        private static bool ArtifactIsActive = false;
        private static bool calculationSacrifice = false;
        void Awake()
        {
            On.RoR2.UI.HUD.Awake += HUDAwake;

            try
            {
                SetUpMiniRPC();
            }
            catch (Exception)
            {
                Debug.LogError($"[SirHamburger] Error in SetUpMiniRPC");
            }
        }
        private void SetUpModCanvas()
        {
            if (ModCanvas == null)
            {
                ModCanvas = new GameObject("UIModifierCanvas");
                ModCanvas.AddComponent<Canvas>();
                ModCanvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceCamera;
                if (HUDRoot != null)
                {
                    ModCanvas.GetComponent<Canvas>().worldCamera = HUDRoot.transform.root.gameObject.GetComponent<Canvas>().worldCamera;
                }

                ModCanvas.AddComponent<CanvasScaler>();
                ModCanvas.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                ModCanvas.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920, 1080);
                ModCanvas.GetComponent<CanvasScaler>().screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            }
        }

        #region Exp bar GameObjects
        public Transform HUDRoot = null;
        public GameObject ModExpBarGroup = null;
        public static List<GameObject> listGainedImages = new List<GameObject>();
        public static List<GameObject> listLostImages = new List<GameObject>();
        public static GameObject itemGainBar;
        public static GameObject itemGainFrame;

        #endregion
        public void HUDAwake(On.RoR2.UI.HUD.orig_Awake orig, RoR2.UI.HUD self)
        {
            orig(self);

            if (!ArtifactIsActive)
                return;

            HUDRoot = self.transform.root;


            MainExpBarStart();

        }

        private void MainExpBarStart()
        {
            //Debug.LogError("MainExpBarStart");
            //Debug.LogError("AArtifactIsActiv " + ArtifactIsActiv);
            if (HUDRoot != null)
            {
                try
                {
                    SetUpModCanvas();
                    listGainedImages.Clear();
                    listLostImages.Clear();
                    float baseSize = (Convert.ToSingle(ArtifactOfDoomConfig.sizeOfSideBars.Value));
                    
                   
                    float screenResultuionMultiplier= (float)Screen.currentResolution.width/(float)Screen.currentResolution.height;
                    //Debug.LogError("screenResultuionMultiplier " +screenResultuionMultiplier);
                    //Debug.LogError("float screenResultuionMultiplier=Screen.currentResolution.width/Screen.currentResolution.height;" +Screen.currentResolution.width/Screen.currentResolution.height);
                     float baseSizeY = baseSize * screenResultuionMultiplier;
                     float baseSizeYPlusMargin = baseSizeY + (float)0.01;
                    //Debug.LogError("baseSizeY " +baseSizeY);
                    //Debug.LogError("baseSize " +baseSize);

                    for (int i = 0; i < 10; i++)
                    {
                        ModExpBarGroup = new GameObject("GainedItems" + i);

                        ModExpBarGroup.transform.SetParent(ModCanvas.transform);

                        ModExpBarGroup.AddComponent<RectTransform>();

                        ModExpBarGroup.GetComponent<RectTransform>().anchorMin = new Vector2(0.0f, (float)(0.20 + ((float)i * baseSizeYPlusMargin)));
                        ModExpBarGroup.GetComponent<RectTransform>().anchorMax = new Vector2(baseSize, (float)(0.2+ baseSizeY + ((float)i * baseSizeYPlusMargin)));
                        ModExpBarGroup.GetComponent<RectTransform>().sizeDelta = Vector2.zero;
                        ModExpBarGroup.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;
                        ModExpBarGroup.AddComponent<NetworkIdentity>().serverOnly = false;
                        listGainedImages.Add(ModExpBarGroup);


                        ModExpBarGroup = new GameObject("LostItems" + i);

                        ModExpBarGroup.transform.SetParent(ModCanvas.transform);

                        ModExpBarGroup.AddComponent<RectTransform>();
                        ModExpBarGroup.GetComponent<RectTransform>().anchorMin = new Vector2((float)1.0-baseSize, (float)(0.20 + ((float)i * baseSizeYPlusMargin)));
                        ModExpBarGroup.GetComponent<RectTransform>().anchorMax = new Vector2(1.00f, (float)(0.2+baseSizeY + ((float)i * baseSizeYPlusMargin)));
                        ModExpBarGroup.GetComponent<RectTransform>().sizeDelta = Vector2.zero;
                        ModExpBarGroup.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;
                        ModExpBarGroup.AddComponent<NetworkIdentity>().serverOnly = false;

                        listLostImages.Add(ModExpBarGroup);
                    }

                    if (!ArtifactOfDoomConfig.disableItemProgressBar.Value && !calculationSacrifice)
                    {
                        ModExpBarGroup = new GameObject("ItemGainBar");
                        ModExpBarGroup.transform.SetParent(ModCanvas.transform);
                        ModExpBarGroup.AddComponent<RectTransform>();
                        ModExpBarGroup.GetComponent<RectTransform>().anchorMin = new Vector2(0.35f, 0.05f);
                        ModExpBarGroup.GetComponent<RectTransform>().anchorMax = new Vector2(0.35f, 0.06f);
                        ModExpBarGroup.GetComponent<RectTransform>().sizeDelta = Vector2.zero;
                        ModExpBarGroup.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;
                        ModExpBarGroup.AddComponent<NetworkIdentity>().serverOnly = false;

                        itemGainBar = ModExpBarGroup;
                        itemGainBar.AddComponent<Image>();
                        itemGainBar.GetComponent<Image>().color = new Color(255, 255, 255, 0.3f);



                        ModExpBarGroup = new GameObject("ItemGainFrame");
                        ModExpBarGroup.transform.SetParent(ModCanvas.transform);
                        ModExpBarGroup.AddComponent<RectTransform>();
                        ModExpBarGroup.GetComponent<RectTransform>().anchorMin = new Vector2(0.35f, 0.05f);
                        ModExpBarGroup.GetComponent<RectTransform>().anchorMax = new Vector2(0.65f, 0.06f);
                        ModExpBarGroup.GetComponent<RectTransform>().sizeDelta = Vector2.zero;
                        ModExpBarGroup.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;
                        ModExpBarGroup.AddComponent<NetworkIdentity>().serverOnly = false;
                        itemGainFrame = ModExpBarGroup;
                        itemGainFrame.AddComponent<Image>();
                        itemGainFrame.GetComponent<Image>().color = new Color(255, 0, 0, 0.1f);
                    }



                }
                catch (Exception)
                {
                    Debug.Log($"[SirHamburger Error] while Adding UI elements");
                }
            }
            else
            {
                Debug.LogError("HUDRoot == null");
            }

        }

        public static IRpcFunc<UpdatePlayerItemsRequest, RpcResult> UpdatePlayerItems { get; set; }
        public static IRpcFunc<UpdateProgressBarRequest, RpcResult> UpdateProgressBar { get; set; }
        public static IRpcFunc<bool, RpcResult> IsArtifactActive { get; set; }
        public static IRpcFunc<bool, RpcResult> IsCalculationSacrifice { get; set; }

        public const string ModGuid = "com.SirHamburger.ArtifactOfDoom";
        private void SetUpMiniRPC()
        {
            // Fix the damn in-game console stealing our not-in-game consoles output.
            // Not related to the demo, just very useful.
            //On.RoR2.RoR2Application.UnitySystemConsoleRedirector.Redirect += orig => { };

            // Create a MiniRpcInstance that automatically registers all commands to our ModGuid
            // This lets us support multiple mods using the same command ID
            // We could also just generate new command ID's without "isolating" them by mod as well, so it would break if mod load order was different for different clients
            // I opted for the ModGuid instead of an arbitrary number or GUID to encourage mods not to set the same ID
            var miniRpc = MiniRpc.CreateInstance(ModGuid);

            UpdatePlayerItems = miniRpc.RegisterFunc(Target.Client, (NetworkUser user, UpdatePlayerItemsRequest request) => //--------------------HierSTuffMachen!!
            {
                var result = new RpcResult();
                try
                {
                    if (ArtifactOfDoomConfig.disableSideBars.Value)
                    {
                        result.Success = false;
                        result.Message = "Artifact of Doom sidebars are disabled, cannot display player item change";
                        result.Severity = LogSeverity.Warning;
                        return result;
                    }

                    int i = 0;
                    foreach (var itemName in request.ItemNames)
                    {
                        if (string.IsNullOrEmpty(itemName))
                        {
                            continue;
                        }

                        if (request.ChangeAction == ItemChangeAction.Gain)
                        {
                            if (listGainedImages[i].GetComponent<Image>() == null)
                            {
                                listGainedImages[i].AddComponent<Image>();
                            }

                            listGainedImages[i].GetComponent<Image>().sprite = ItemCatalog.GetItemDef(ItemCatalog.FindItemIndex(itemName)).pickupIconSprite;
                        }
                        else if (request.ChangeAction == ItemChangeAction.Loss)
                        {
                            if (listLostImages[i].GetComponent<Image>() == null)
                            {
                                listLostImages[i].AddComponent<Image>();
                            }
                            listLostImages[i].GetComponent<Image>().sprite = ItemCatalog.GetItemDef(ItemCatalog.FindItemIndex(itemName)).pickupIconSprite;
                        }

                        i++;

                        result.Success = true;
                    }
                }
                catch (Exception e)
                {
                    result.Success = false;
                    result.Message = $"Error updating player items! {e}";
                    result.Severity = LogSeverity.Error;
                }

                return result;
            });

            UpdateProgressBar = miniRpc.RegisterFunc(Target.Client, (NetworkUser user, UpdateProgressBarRequest request) => //--------------------HierSTuffMachen!!
            {
                var result = new RpcResult();
                if (ArtifactOfDoomConfig.disableItemProgressBar.Value || calculationSacrifice)
                {
                    result.Message = "Progress Bar is disabled";
                    result.Severity = LogSeverity.Warning;
                    return result;
                }

                if (request == null)
                {
                    result.Message = "UpdateProgressBarRequest is null!";
                    result.Severity = LogSeverity.Error;
                    return result;
                }
                
                if (request.TriggerAmount <= 0)
                {
                    result.Message = "UpdateProgressBarRequest TriggerAmount is not valid!";
                    result.Severity = LogSeverity.Error;
                    return result;
                }

                if (itemGainBar == null)
                {
                    result.Message = "itemGainBar has not been initialised by UI!";
                    result.Severity = LogSeverity.Error;
                    return result;
                }

                double progress = (double)request.EnemiesKilled / request.TriggerAmount;

                if (itemGainBar.GetComponent<RectTransform>() == null)
                {
                    result.Message = "itemGainBar RectTransform Get failed!";
                    result.Severity = LogSeverity.Error;
                    return result;
                }

                if (itemGainBar.GetComponent<RectTransform>().anchorMax == null || itemGainBar.GetComponent<RectTransform>().anchorMax == null)
                {
                    result.Message = "itemGainBar RectTransform properties have not been initialised!";
                    result.Severity = LogSeverity.Error;
                    return result;
                }

                if ((0.35f + (float)(progress * 0.3)) > 0.65f)
                {
                    itemGainBar.GetComponent<RectTransform>().anchorMax = new Vector2(0.65f, 0.06f);
                }
                else
                {
                    itemGainBar.GetComponent<RectTransform>().anchorMin = new Vector2(0.35f, 0.05f);

                    itemGainBar.GetComponent<RectTransform>().anchorMax = new Vector2(0.35f + (float)(progress * 0.3), 0.06f);
                }

                result.Success = true;
                return result;
            });

            IsArtifactActive = miniRpc.RegisterFunc(Target.Client, (NetworkUser user, bool isActive) => //--------------------HierSTuffMachen!!
            {
                ArtifactIsActive = isActive;
                var result = new RpcResult(true, "UI aware Artifact of Doom is active!", LogSeverity.Info);

                return result;
            });

            IsCalculationSacrifice = miniRpc.RegisterFunc(Target.Client, (NetworkUser user, bool isActive) => //--------------------HierSTuffMachen!!
            {
                calculationSacrifice = isActive;
                var result = new RpcResult(true, "UI aware Artifact of Sacrifice calculation is in use!", LogSeverity.Info);

                return result;
            });

            Debug.LogWarning("minirpc succsessfull set up");
        }

        private void OnDestroy()
        {
            On.RoR2.UI.HUD.Awake -= HUDAwake;
        }
    }
}
