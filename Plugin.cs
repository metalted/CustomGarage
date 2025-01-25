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
using System.Reflection;

namespace CustomGarage
{
    public struct Location
    {
        public Vector3 position;
        public Quaternion rotation;
        public float FOV;
        public bool IsOrthographic;
    }

    [BepInPlugin(pluginGUID, pluginName, pluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string pluginGUID = "com.metalted.zeepkist.customgarage";
        public const string pluginName = "Custom Garage";
        public const string pluginVersion = "1.2";

        public static Plugin Instance;

        public string pluginPath;
        public string blueprintPath;

        public ConfigEntry<bool> pluginEnabled;
        public ConfigEntry<string> garageBlueprintName;
        public ConfigEntry<bool> allowSeasonal;
        public ConfigEntry<bool> keepOriginalScenery;
        public ConfigEntry<bool> blueprintGarageVisible;
        public ConfigEntry<bool> useCustomCameras;
        public ConfigEntry<bool> keepCat;

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
            keepCat = Config.Bind("Settings", "Keep Cat Active", true, "When Keep Original Scenery is off, prevent the cat from being hidden as well.");

            // Plugin startup logic
            Logger.LogInfo($"Plugin {PluginInfo.PLUGIN_GUID} is loaded!");
        }

        public void OnMainMenu()
        {
            //If the plugin is not enabled, stop executing.
            if (!pluginEnabled.Value)
            {
                //Make sure to clear some variables.
                usingCustomCameras = false;
                cameraLocations.Clear();
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
            if (!zeepFile.Valid)
            {
                Debug.LogError("Blueprint not valid");
                return;
            }

            //Find the garage block in the blueprint
            bool containsBlock = zeepFile.Blocks.Any(block => block.BlockID == 2290);
            if (!containsBlock)
            {
                Debug.LogError("Blueprint doesnt contain the garage block");
                return;
            }

            //Instantiate the blueprint
            GameObject bpCopy = InstantiateBlueprint(zeepFile);

            //Find the special blocks:
            //Garage block, first one is the alignment one, others are treated like a normal block.
            //Camera blocks, all are used for positioning.
            //Cat spawner, for setting the cat position. First one is for positioning, others are treated like a normal block.
            //Spike spawner, for setting the skybox rotation. First one is for rotation, others are treated like a normal block.
            BlockProperties garageBlockProperties = null;
            List<BlockProperties> tempCamBps = new List<BlockProperties>();
            BlockProperties catSpawner = null;
            BlockProperties spikeSpawner = null;

            foreach (Transform child in bpCopy.transform)
            {
                BlockProperties blockProperties = child.GetComponent<BlockProperties>();

                if(blockProperties != null)
                {
                    switch(blockProperties.blockID)
                    {
                        case 2290:
                            //Garage
                            if(garageBlockProperties == null)
                            {
                                garageBlockProperties = blockProperties;
                            }
                            break;
                        case 2008:
                            //Camera
                            if (useCustomCameras.Value)
                            {
                                tempCamBps.Add(blockProperties);
                            }
                            break;
                        case 42:
                            //Cat spawner
                            if(catSpawner == null)
                            {
                                catSpawner = blockProperties;
                                catSpawner.gameObject.SetActive(false);
                            }
                            break;
                        case 281:
                            //Spike spawner
                            if(spikeSpawner == null)
                            {
                                spikeSpawner = blockProperties;
                                spikeSpawner.gameObject.SetActive(false);
                            }
                            break;
                    }
                }
            }

            //Shouldnt happen but the garage block properties is required.
            if (garageBlockProperties == null)
            {
                Debug.Log("Couldn't find garage blockproperties!");
                GameObject.Destroy(bpCopy);
                return;
            }

            //Calculate the transform placement for the blueprint garage.
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

            //Hide the blueprint garage according to the setting.
            if (!blueprintGarageVisible.Value)
            {
                garageBlockProperties.gameObject.SetActive(false);
            }

            // Get all root objects in the active scene
            GameObject[] rootObjects = SceneManager.GetActiveScene().GetRootGameObjects();

            //Go over all the objects.
            foreach (GameObject obj in rootObjects)
            {
                //If we dont want to keep the original scenery.
                if (!keepOriginalScenery.Value)
                {
                    //Find the object called House.
                    if (obj.name == "House")
                    {
                        // Go over all children in "House"
                        foreach (Transform child in obj.transform)
                        {
                            //Hide the time based cosmetics according to the config.
                            if (child.name == "TIME BASED COSMETICS")
                            {
                                if (!allowSeasonal.Value)
                                {
                                    child.gameObject.SetActive(false);
                                }
                            }
                            //Always keep the soapbox model.
                            else if (child.name == "Soapbox Model Showoff With Physics")
                            {
                                //Do nothing
                            }
                            //Hide everything else from the house scenery.
                            else
                            {
                                child.gameObject.SetActive(false);
                            }
                        }
                    }

                    //Find the scenery object.
                    if (obj.name == "Scenery")
                    {
                        //Make sure the cat is taken out before we hide the object.
                        if(keepCat.Value)
                        {
                            Transform catTransform = null;

                            //Take out the cat
                            foreach (Transform child in obj.transform)
                            {
                                if (child.name == "Cat (1)")
                                {
                                    catTransform = child;
                                }
                            }

                            //If we found the cat, and there is a cat spawner block present, place the cat at the spawners position.
                            if(catTransform != null)
                            {
                                catTransform.parent = obj.transform.parent;
                                catTransform.gameObject.SetActive(true);

                                if(catSpawner != null)
                                {
                                    catTransform.position = catSpawner.transform.position;
                                }
                            }
                        }

                        obj.SetActive(false);
                    }
                }
            }

            //Find the skybox manager and set the skybox
            SkyboxManager skybox = GameObject.FindObjectOfType<SkyboxManager>(true);
            if (skybox != null)
            {
                //If a spike spawner is present, set the y-rotation of the skybox manager to match that of the spike spawner object
                if (spikeSpawner != null)
                {
                    Camera.main.transform.GetChild(0).transform.Rotate(0, spikeSpawner.transform.eulerAngles.y, 0);
                }

                skybox.SetToSkybox(zeepFile.Header.Skybox, true);
            }

           

            //Sort the cameras by their body color to determine the order.
            tempCamBps.Sort((a, b) => a.properties[10].CompareTo(b.properties[10]));

            //If the list is empty we cant use custom cameras.
            usingCustomCameras = tempCamBps.Count != 0;

            if (usingCustomCameras)
            {
                //Clear the camera list
                cameraLocations.Clear();

                //Create the list of 6 locations based on the sorted array
                for (int i = 0; i < 6; i++)
                {
                    BlockProperties camUsed = tempCamBps[i % tempCamBps.Count];
                    Location camLocation = new Location()
                    {
                        position = camUsed.transform.position,
                        rotation = camUsed.transform.rotation * Quaternion.Euler(0, 180, 0),
                        FOV = Mathf.Clamp(camUsed.properties[9], 1f, 180f),
                        IsOrthographic = Mathf.RoundToInt(camUsed.properties[11]) == 4

                    };
                    cameraLocations.Add(camLocation);
                }

                //Hide the camera models
                for (int i = 0; i < tempCamBps.Count; i++)
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
            if (zoomIn)
            {
                instance.MenuObject.SetActive(false);
            }

            Plugin.Instance.SetCameraToLocation(instance, instance.CurrentPosition);

            if (!zoomIn)
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

            Plugin.Instance.SetCameraToLocation(instance, instance.CurrentPosition);

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

            Plugin.Instance.SetCameraToLocation(instance, instance.CurrentPosition);

            instance.MenuObject.SetActive(true);
            instance.UpdateButtonText();
            instance.State = MainMenuUI.MenuState.None;
        }

        public void SetCameraToLocation(MainMenuUI instance, int index)
        {
            instance.MenuCamera.transform.position = cameraLocations[index].position;
            instance.MenuCamera.transform.rotation = cameraLocations[index].rotation;
            if(cameraLocations[index].IsOrthographic)
            {
                instance.MenuCamera.orthographic = true;
                instance.MenuCamera.orthographicSize = cameraLocations[index].FOV;
            }
            else
            {
                instance.MenuCamera.orthographic = false;
                instance.MenuCamera.fieldOfView = cameraLocations[index].FOV;
            }
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

    [HarmonyPatch(typeof(MainMenuUI), "StartCamera")]
    public class MainMenuUIStartCameraPatch
    {
        public static bool Prefix(MainMenuUI __instance)
        {
            //Continue original codes if using custom cameras is off.
            if (!Plugin.Instance.usingCustomCameras)
            {
                return true;
            }

            //Patched zeepkist code.
            Plugin.Instance.SetCameraToLocation(__instance, __instance.CurrentPosition);

            if (!AnimateWhitePanel.IsWhiteCirclePanelActive)
            {
                return false;
            }
            AnimateWhitePanel.AnimateTheCircle(false, 0.9f, 0.0f, false);
            return false;
        }
    }

    [HarmonyPatch(typeof(MainMenuUI), "Update")]
    public class MainMenuUIUpdatePatch
    {
        public static bool Prefix(MainMenuUI __instance)
        {
            //Continue original codes if using custom cameras is off.
            if (!Plugin.Instance.usingCustomCameras)
            {
                return true;
            }

            //Patched zeepkist code.
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
            //Continue original codes if using custom cameras is off.
            if (!Plugin.Instance.usingCustomCameras)
            {
                return true;
            }

            //Patched zeepkist code.

            // Call the base method using reflection
            MethodInfo baseOnOpen = typeof(MainMenuUI).BaseType.GetMethod("OnOpen", BindingFlags.Instance | BindingFlags.NonPublic);
            if (baseOnOpen != null)
            {
                baseOnOpen.Invoke(__instance, null);
            }

            __instance.UpdateButtonText();
            __instance.Navigator.navigatorActive = false;
            if (PlayerManager.Instance.exitFromLevelEditor)
            {
                PlayerManager.Instance.exitFromLevelEditor = false;
                __instance.CurrentPosition = __instance.LevelEditorPosition;
                Plugin.Instance.SetCameraToLocation(__instance, __instance.CurrentPosition);
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
            //Continue original codes if using custom cameras is off.
            if (!Plugin.Instance.usingCustomCameras)
            {
                return true;
            }

            //Patched zeepkist code.
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
            //Continue original codes if using custom cameras is off.
            if (!Plugin.Instance.usingCustomCameras)
            {
                return true;
            }

            //Patched zeepkist code.
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
            //Continue original codes if using custom cameras is off.
            if (!Plugin.Instance.usingCustomCameras)
            {
                return true;
            }

            //Patched zeepkist code.
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
            //Continue original codes if using custom cameras is off.
            if (!Plugin.Instance.usingCustomCameras)
            {
                return true;
            }

            //Patched zeepkist code.
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
