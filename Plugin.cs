using BepInEx;
using HarmonyLib;
using BepInEx.Configuration;
using System;
using System.IO;
using System.Linq;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

namespace CustomGarage
{
    [BepInPlugin(pluginGUID, pluginName, pluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string pluginGUID = "com.metalted.zeepkist.customgarage";
        public const string pluginName = "Custom Garage";
        public const string pluginVersion = "1.0";

        public static Plugin Instance;

        public string pluginPath;
        public string blueprintPath;

        public ConfigEntry<bool> pluginEnabled;
        public ConfigEntry<string> garageBlueprintName;
        public ConfigEntry<bool> allowSeasonal;
        public ConfigEntry<bool> keepOriginalScenery;
        public ConfigEntry<bool> blueprintGarageVisible;

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

            foreach (Transform child in bpCopy.transform)
            {
                BlockProperties blockProperties = child.GetComponent<BlockProperties>();
                if (blockProperties != null && blockProperties.blockID == 2290)
                {
                    garageBlockProperties = blockProperties;
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
    }

    [HarmonyPatch(typeof(MainMenuUI), "Awake")]
    public class MainMenuUIAwakePatch
    {
        public static void Postfix()
        {
            Plugin.Instance.OnMainMenu();
        }
    }
}
