using BepInEx;
using HarmonyLib;
using BepInEx.Configuration;
using System;
using System.IO;
using System.Linq;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using FMODSyntax;

namespace CustomGarage
{
    public struct Location
    {
        public Vector3 position;
        public Quaternion rotation;
    }

    [BepInPlugin(pluginGUID, pluginName, pluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string pluginGUID = "com.metalted.zeepkist.customgarage";
        public const string pluginName = "Custom Garage";
        public const string pluginVersion = "1.1";

        public static Plugin Instance;

        public string pluginPath;
        public string blueprintPath;

        public ConfigEntry<bool> pluginEnabled;
        public ConfigEntry<string> garageBlueprintName;
        public ConfigEntry<bool> allowSeasonal;
        public ConfigEntry<bool> keepOriginalScenery;
        public ConfigEntry<bool> blueprintGarageVisible;
        public ConfigEntry<bool> useCustomCameras;

        //Camera paints: reel/outer ring, camera body, inner reel/inner ring
        public bool usingCustomCameras = false;
        public List<Location> cameraLocations = new List<Location>();

        private void Awake()
        {
            Instance = this;

            Harmony harmony = new Harmony(pluginGUID);
            harmony.PatchAll();

            pluginPath = AppDomain.CurrentDomain.BaseDirectory + @"\BepInEx\plugins";
            blueprintPath = Path.Combine(pluginPath, "Blueprints");

            pluginEnabled = Config.Bind("Settings", "Plugin Enabled", true, "Should the plugin be active?");
            garageBlueprintName = Config.Bind("Settings", "Garage Blueprint Name", "", "The name to the blueprint to be used for the garage. Make sure it is unique.");
            allowSeasonal = Config.Bind("Settings", "Allow Seasonal Items", false, "Should seasonal items be spawned?");
            keepOriginalScenery = Config.Bind("Settings", "Keep Original Scenery", false, "Should the original scenery be visible?");
            blueprintGarageVisible = Config.Bind("Settings", "Blueprint Garage Visible", true, "Should the blueprint garage be visible (for open world menu)");
            useCustomCameras = Config.Bind("Settings", "Use Custom Camera Positions", false, "Do we use the regular camera's, or the in the blueprint defined camera positions?");

            // Plugin startup logic
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
        }

        public void OnMainMenu()
        {
            //If the plugin is not enabled, stop executing.
            if(!pluginEnabled.Value)
            {
                return;
            }

            //Check if the blueprint exists. Try to find the file with the given name.
            string bpName = garageBlueprintName.Value.Replace(".zeeplevel", "") + ".zeeplevel";
            string blueprintFilePath = Directory.EnumerateFiles(blueprintPath, bpName, SearchOption.AllDirectories).FirstOrDefault();
            if (blueprintFilePath == null)
            {
                Debug.LogError("Blueprint not found");
                return;
            }

            //Read the blueprint file
            ZeeplevelFile zeepFile = new ZeeplevelFile(blueprintFilePath);
            if(!zeepFile.Valid)
            {
                Debug.LogError("Blueprint not valid");
                return;
            }

            //Find the garage block in the blueprint
            bool containsBlock = zeepFile.Blocks.Any(block => block.BlockID == 2290);
            if(!containsBlock)
            {
                Debug.LogError("Blueprint doesnt contain the garage block");
                return;
            }

            //Instantiate the blueprint
            GameObject bpCopy = InstantiateBlueprint(zeepFile);

            BlockProperties garageBlockProperties = null;
            List<BlockProperties> tempCamBps = new List<BlockProperties>();

            foreach (Transform child in bpCopy.transform)
            {
                BlockProperties blockProperties = child.GetComponent<BlockProperties>();
                if (blockProperties != null && blockProperties.blockID == 2290)
                {
                    garageBlockProperties = blockProperties;
                    break;
                }

                if (blockProperties != null && blockProperties.blockID == 2008)
                {
                    if(useCustomCameras.Value)
                    {
                        tempCamBps.Add(blockProperties);
                    }
                    break;
                }
            }

            //Shouldnt happen but the garage block properties is required.
            if (garageBlockProperties == null)
            {
                Debug.Log("Couldn't find garage blockproperties!");
                GameObject.Destroy(bpCopy);
                return;
            }

            Vector3 targetPosition = new Vector3(-0.036f, 6.358f, 7.931f);
            float scaleFactor = 0.33333f / garageBlockProperties.gameObject.transform.localScale.x;
            float garageRotation = garageBlockProperties.gameObject.transform.localEulerAngles.y;

            // Normalize garageRotation to range [0, 360] for consistent calculations
            garageRotation = (garageRotation + 360) % 360;

            // Calculate the correction to align rotation to 90 degrees
            float rotationCorrection = 90 - garageRotation;

            // Step 1: Scale the parent to ensure the child's scale is correct
            bpCopy.transform.localScale *= scaleFactor;

            // Step 2: Rotate the parent to ensure the child's rotation aligns
            bpCopy.transform.Rotate(0, rotationCorrection, 0, Space.World);

            // Step 3: Adjust the parent position so the child ends up at the target position
            Vector3 garageShift = garageBlockProperties.gameObject.transform.position - bpCopy.transform.position;
            bpCopy.transform.position = targetPosition - garageShift;
            
            if(!blueprintGarageVisible.Value)
            {
                garageBlockProperties.gameObject.SetActive(false);
            }

            // Get all root objects in the active scene
            GameObject[] rootObjects = SceneManager.GetActiveScene().GetRootGameObjects();

            foreach (GameObject obj in rootObjects)
            {
                if (!keepOriginalScenery.Value)
                {
                    if (obj.name == "House")
                    {
                        // Go over all children in "House"
                        foreach (Transform child in obj.transform)
                        {
                            if (child.name == "TIME BASED COSMETICS")
                            {
                                if(!allowSeasonal.Value)
                                {
                                    child.gameObject.SetActive(false);
                                }
                            }
                            else if (child.name == "Soapbox Model Showoff With Physics")
                            {
                                //Do nothing
                            }
                            else
                            {
                                child.gameObject.SetActive(false);
                            }
                        }
                    }

                    if(obj.name == "Scenery")
                    {
                        obj.SetActive(false);
                    }
                }
            }

            //Find the skybox manager and set the skybox
            SkyboxManager skybox = GameObject.FindObjectOfType<SkyboxManager>(true);
            if(skybox != null)
            {
                skybox.SetToSkybox(zeepFile.Header.Skybox, true);
            }

            //Sort the cameras by their body color to determine the order.
            tempCamBps.Sort((a, b) => a.properties[10].CompareTo(b.properties[10]));

            //If the list is empty we cant use custom cameras.
            usingCustomCameras = tempCamBps.Count != 0;

            if(usingCustomCameras)
            {
                //Clear the camera list
                cameraLocations.Clear();
                
                //Create the list of 6 locations based on the sorted array
                for (int i = 0; i < 6; i++)
                {
                    BlockProperties camUsed = tempCamBps[i % tempCamBps.Count];
                    Location camLocation = new Location() { position = camUsed.transform.position, rotation = camUsed.transform.rotation };
                    cameraLocations.Add(camLocation);
                }

                //Hide the camera models
                for(int i = 0; i < tempCamBps.Count; i++)
                {
                    tempCamBps[i].gameObject.SetActive(false);
                }
            }
        }

        public GameObject InstantiateBlueprint(ZeeplevelFile zeeplevelFile)
        {
            GameObject bpParent = new GameObject("GarageBlueprint");

            for (int i = 0; i < zeeplevelFile.Blocks.Count; i++)
            {
                int id = zeeplevelFile.Blocks[i].BlockID;

                //Skip invalid block ids
                if (id < 0 || id >= PlayerManager.Instance.loader.globalBlockList.blocks.Count)
                {
                    continue;
                }

                //Create a blockPropertyJSON of the ZeeplevelBlock
                BlockPropertyJSON blockPropertyJSON = ZeeplevelBlockToBlockPropertyJSON(zeeplevelFile.Blocks[i]);

                BlockProperties theNewBlock = GameObject.Instantiate<BlockProperties>(PlayerManager.Instance.loader.globalBlockList.blocks[id]);
                theNewBlock.gameObject.name = PlayerManager.Instance.loader.globalBlockList.blocks[id].gameObject.name;
                theNewBlock.CreateBlock();
                theNewBlock.properties.Clear();
                theNewBlock.isEditor = true;
                theNewBlock.LoadProperties_v15(blockPropertyJSON, false);
                theNewBlock.isLoading = false;

                theNewBlock.transform.parent = bpParent.transform;
            }

            return bpParent;
        }

        public BlockPropertyJSON ZeeplevelBlockToBlockPropertyJSON(ZeeplevelBlock block)
        {
            BlockPropertyJSON blockPropertyJSON = new BlockPropertyJSON();
            blockPropertyJSON.position = new Vector3(block.Position.x, block.Position.y, block.Position.z);
            blockPropertyJSON.eulerAngles = new Vector3(block.Rotation.x, block.Rotation.y, block.Rotation.z);
            blockPropertyJSON.localScale = new Vector3(block.Scale.x, block.Scale.y, block.Scale.z);
            blockPropertyJSON.properties = new List<float>();
            foreach (float f in block.Properties)
            {
                blockPropertyJSON.properties.Add(f);
            }
            blockPropertyJSON.blockID = block.BlockID;

            return blockPropertyJSON;
        }

        public void Zoom(MainMenuUI instance, bool zoomIn)
        {
            if(zoomIn)
            {
                instance.MenuObject.SetActive(false);
            }

            instance.MenuCamera.transform.position = Plugin.Instance.cameraLocations[instance.CurrentPosition].position;
            instance.MenuCamera.transform.rotation = Plugin.Instance.cameraLocations[instance.CurrentPosition].rotation;

            if(!zoomIn)
            {
                instance.MenuObject.SetActive(true);
            }
            else
            {
                instance.UIManager.OpenUI<BaseUI>(instance.PositionUIs[instance.CurrentPosition]);
            }

            instance.ZoomedIn = zoomIn;
            instance.State = MainMenuUI.MenuState.None;
        }

        public void Next(MainMenuUI instance)
        {
            instance.CurrentPosition++;

            if (instance.CurrentPosition >= instance.PositionCameras.Count)
            {
                instance.CurrentPosition = 0;
            }

            instance.MenuCamera.transform.position = Plugin.Instance.cameraLocations[instance.CurrentPosition].position;
            instance.MenuCamera.transform.rotation = Plugin.Instance.cameraLocations[instance.CurrentPosition].rotation;
            
            instance.MenuObject.SetActive(true);
            instance.UpdateButtonText();
            instance.State = MainMenuUI.MenuState.None;
        }

        public void Previous(MainMenuUI instance)
        {
            instance.CurrentPosition--;

            if (instance.CurrentPosition < 0)
            {
                instance.CurrentPosition = instance.PositionCameras.Count - 1;
            }

            instance.MenuCamera.transform.position = Plugin.Instance.cameraLocations[instance.CurrentPosition].position;
            instance.MenuCamera.transform.rotation = Plugin.Instance.cameraLocations[instance.CurrentPosition].rotation;
            
            instance.MenuObject.SetActive(true);
            instance.UpdateButtonText();
            instance.State = MainMenuUI.MenuState.None;
        }
        
    }

    [HarmonyPatch(typeof(MainMenuUI), "Awake")]
    public class MainMenuUIAwakePatch
    {
        public static void Postfix()
        {
            Plugin.Instance.OnMainMenu();
        }
    }

    [HarmonyPatch(typeof(MainMenuUI), "Start")]
    public class MainMenuUIStartPatch
    {
        public static bool Prefix(MainMenuUI __instance)
        {
            if(!Plugin.Instance.usingCustomCameras)
            {
                return true;
            }

            __instance.MenuCamera.transform.position = Plugin.Instance.cameraLocations[__instance.CurrentPosition].position;
            __instance.MenuCamera.transform.rotation = Plugin.Instance.cameraLocations[__instance.CurrentPosition].rotation;

            if (AnimateWhitePanel.IsWhiteCirclePanelActive)
            {
                AnimateWhitePanel.AnimateTheCircle(false, 0.9f, 0.0f, false);
            }

            __instance.UpdateButtonText();
            return false;
        }
    }

    [HarmonyPatch(typeof(MainMenuUI), "Update")]
    public class MainMenuUIUpdatePatch
    {
        public static bool Prefix(MainMenuUI __instance)
        {
            if (!Plugin.Instance.usingCustomCameras)
            {
                return true;
            }

            if ((__instance.MenuLeft.buttonDown || __instance.State == MainMenuUI.MenuState.GoPrevious && __instance.MenuAccept.buttonDown) && !__instance.ZoomedIn && (!__instance.PositionCameras[__instance.CurrentPosition].invertedOrientation || !__instance.GameSettings.menu_controls_camera_relative) || (__instance.MenuRight.buttonDown || __instance.State == MainMenuUI.MenuState.GoPrevious && __instance.MenuAccept.buttonDown) && !__instance.ZoomedIn && __instance.PositionCameras[__instance.CurrentPosition].invertedOrientation && __instance.GameSettings.menu_controls_camera_relative)
            {
                __instance.GoPrevious(true);
                AudioEvents.MenuClick.Play((Transform)null);
            }
            else if ((__instance.MenuRight.buttonDown || __instance.State == MainMenuUI.MenuState.GoNext && __instance.MenuAccept.buttonDown) && !__instance.ZoomedIn && (!__instance.PositionCameras[__instance.CurrentPosition].invertedOrientation || !__instance.GameSettings.menu_controls_camera_relative) || (__instance.MenuLeft.buttonDown || __instance.State == MainMenuUI.MenuState.GoNext && __instance.MenuAccept.buttonDown) && !__instance.ZoomedIn && __instance.PositionCameras[__instance.CurrentPosition].invertedOrientation && __instance.GameSettings.menu_controls_camera_relative)
            {
                __instance.GoNext(true);
                AudioEvents.MenuClick.Play((Transform)null);
            }
            else if (__instance.MenuAccept.buttonDown && !__instance.ZoomedIn)
            {
                __instance.ZoomIn();
                AudioEvents.MenuClick.Play((Transform)null);
            }
            else
            {
                if (!__instance.MenuAccept.buttonDown && !__instance.MenuCancel.buttonDown && !__instance.Escape.buttonDown && !__instance.Pause.buttonDown || !__instance.ZoomedIn)
                    return false;
                __instance.ZoomOut();
                AudioEvents.MenuClick.Play((Transform)null);
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(MainMenuUI), "OnOpen")]
    public class MainMenuUIOnOpenPatch
    {
        public static bool Prefix(MainMenuUI __instance)
        {
            if (!Plugin.Instance.usingCustomCameras)
            {
                return true;
            }

            __instance.OnOpen();
            __instance.UpdateButtonText();
            __instance.Navigator.navigatorActive = false;
            if (PlayerManager.Instance.exitFromLevelEditor)
            {
                PlayerManager.Instance.exitFromLevelEditor = false;
                __instance.CurrentPosition = __instance.LevelEditorPosition;
                __instance.MenuCamera.transform.position = Plugin.Instance.cameraLocations[__instance.CurrentPosition].position;
                __instance.MenuCamera.transform.rotation = Plugin.Instance.cameraLocations[__instance.CurrentPosition].rotation;
                __instance.UpdateButtonText();
            }
            if (!__instance.ZoomedIn)
            {
                return false;
            }

            Plugin.Instance.Zoom(__instance, false);

            return false;
        }
    }

    [HarmonyPatch(typeof(MainMenuUI), "ZoomIn")]
    public class MainMenuUIZoomInPatch
    {
        public static bool Prefix(MainMenuUI __instance)
        {
            if (!Plugin.Instance.usingCustomCameras)
            {
                return true;
            }

            if(__instance.ZoomedIn)
            {
                return false;
            }

            if(__instance.State == MainMenuUI.MenuState.ZoomIn)
            {
                Plugin.Instance.Zoom(__instance, true);
            }
            else
            {
                if(__instance.State != MainMenuUI.MenuState.None)
                {
                    return false;
                }

                Plugin.Instance.Zoom(__instance, true);
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(MainMenuUI), "ZoomOut")]
    public class MainMenuUIZoomOutPatch
    {
        public static bool Prefix(MainMenuUI __instance)
        {
            if (!Plugin.Instance.usingCustomCameras)
            {
                return true;
            }

            if (!__instance.ZoomedIn)
            {
                return false;
            }

            if (__instance.State == MainMenuUI.MenuState.ZoomOut)
            {
                Plugin.Instance.Zoom(__instance, false);
            }
            else
            {
                if (__instance.State != MainMenuUI.MenuState.None)
                {
                    return false;
                }

                Plugin.Instance.Zoom(__instance, false);
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(MainMenuUI), "GoNext")]
    public class MainMenuUIGoNextPatch
    {
        public static bool Prefix(MainMenuUI __instance, ref bool ignoreRelative)
        {
            if (!Plugin.Instance.usingCustomCameras)
            {
                return true;
            }

            if(__instance.ZoomedIn)
            {
                return false;
            }

            if(__instance.State == MainMenuUI.MenuState.GoNext)
            {
                Plugin.Instance.Next(__instance);
            }
            else
            {
                if(__instance.State != MainMenuUI.MenuState.None)
                {
                    return false;
                }

                Plugin.Instance.Next(__instance);
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(MainMenuUI), "GoPrevious")]
    public class MainMenuUIGoPreviousPatch
    {
        public static bool Prefix(MainMenuUI __instance, ref bool ignoreRelative)
        {
            if (!Plugin.Instance.usingCustomCameras)
            {
                return true;
            }

            if (__instance.ZoomedIn)
            {
                return false;
            }

            if (__instance.State == MainMenuUI.MenuState.GoPrevious)
            {
                Plugin.Instance.Previous(__instance);
            }
            else
            {
                if (__instance.State != MainMenuUI.MenuState.None)
                {
                    return false;
                }

                Plugin.Instance.Previous(__instance);
            }

            return false;
        }
    }
}
