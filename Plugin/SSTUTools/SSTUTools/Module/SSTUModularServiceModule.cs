﻿using System;
using System.Collections.Generic;
using UnityEngine;
using KSPShaderTools;

namespace SSTUTools
{

    public class SSTUModularServiceModule : PartModule, IPartMassModifier, IPartCostModifier, IRecolorable
    {

        #region REGION - Standard Part Config Fields

        //for RO rescale use
        [KSPField]
        public float coreDiameter = 2.5f;

        [KSPField]
        public float topDiameter = 1.875f;

        [KSPField]
        public float bottomDiameter = 2.5f;

        [KSPField]
        public bool useAdapterVolume = false;

        [KSPField]
        public bool useAdapterMass = false;

        [KSPField]
        public bool useAdapterCost = false;

        [KSPField]
        public bool updateSolar = true;

        [KSPField]
        public string solarAnimationID = "solarDeploy";

        [KSPField]
        public string topManagedNodes = "top1, top2, top3, top4, top5";

        [KSPField]
        public string bottomManagedNodes = "bottom1, bottom2, bottom3, bottom4, bottom5";

        //persistent config fields for module selections
        //also GUI controls for module selection

        [KSPField(isPersistant = true, guiName = "Top"),
         UI_ChooseOption(suppressEditorShipModified = true)]
        public string currentTop = "Mount-None";

        [KSPField(isPersistant = true, guiName = "Core"),
         UI_ChooseOption(suppressEditorShipModified = true)]
        public string currentCore = "Mount-None";

        [KSPField(isPersistant = true, guiName = "Bottom"),
         UI_ChooseOption(suppressEditorShipModified = true)]
        public string currentBottom = "Mount-None";

        [KSPField(isPersistant = true, guiName = "Solar"),
         UI_ChooseOption(suppressEditorShipModified = true)]
        public string currentSolar = "Solar-None";

        //persistent config fields for module texture sets
        //also GUI controls for texture selection
        [KSPField(isPersistant = true, guiName = "Top Tex"),
         UI_ChooseOption(suppressEditorShipModified = true)]
        public string currentTopTexture = "default";

        [KSPField(isPersistant = true, guiName = "Core Tex"),
         UI_ChooseOption(suppressEditorShipModified = true)]
        public string currentCoreTexture = "default";

        [KSPField(isPersistant = true, guiName = "Bottom Tex"),
         UI_ChooseOption(suppressEditorShipModified = true)]
        public string currentBottomTexture = "default";

        //persistent data for modules; stores colors and other per-module data
        [KSPField(isPersistant = true)]
        public string topModulePersistentData;
        [KSPField(isPersistant = true)]
        public string coreModulePersistentData;
        [KSPField(isPersistant = true)]
        public string bottomModulePersistentData;

        //tracks if default textures and resource volumes have been initialized; only occurs once during the parts first Start() call
        [KSPField(isPersistant = true)]
        public bool initializedDefaults = false;

        //standard work-around for lack of config-node data being passed consistently and lack of support for mod-added serializable classes
        [Persistent]
        public string configNodeData = string.Empty;

        #endregion REGION - Standard Part Config Fields

        #region REGION - Private working vars

        private bool initialized = false;
        private float modifiedMass = 0;
        private float modifiedCost = 0;
        private string[] topNodeNames;
        private string[] bottomNodeNames;
        
        ModelModule<SingleModelData, SSTUModularServiceModule> topModule;
        ModelModule<SingleModelData, SSTUModularServiceModule> coreModule;
        ModelModule<SingleModelData, SSTUModularServiceModule> bottomModule;
        ModelModule<SolarData, SSTUModularServiceModule> solarModule;
        ModelModule<ServiceModuleRCSModelData, SSTUModularServiceModule> rcsModule;

        //animate controlled reference for solar panel animation module
        private SSTUAnimateControlled solarAnimationControl;

        //animate controlled reference for service bay animation module
        private SSTUAnimateControlled bayAnimationControl;

        #endregion ENDREGION - Private working vars

        #region REGION - Standard KSP Overrides

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            if (string.IsNullOrEmpty(configNodeData)) { configNodeData = node.ToString(); }
            initialize(false);
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            initialize(true);

            Action<SSTUModularServiceModule> modelChangedAction = delegate (SSTUModularServiceModule m)
            {
                m.updateModulePositions();
                m.updateMassAndCost();
                m.updateAttachNodes(true);
                m.updateDragCubes();
                m.updateResourceVolume();
                m.updateGUI();
            };

            Fields[nameof(currentTop)].uiControlEditor.onFieldChanged = delegate (BaseField a, System.Object b)
            {
                topModule.modelSelected(currentTop);
                this.actionWithSymmetry(modelChangedAction);
            };

            Fields[nameof(currentCore)].uiControlEditor.onFieldChanged = delegate (BaseField a, System.Object b)
            {
                coreModule.modelSelected(currentCore);
                this.actionWithSymmetry(modelChangedAction);
            };

            Fields[nameof(currentBottom)].uiControlEditor.onFieldChanged = delegate (BaseField a, System.Object b)
            {
                bottomModule.modelSelected(currentBottom);
                this.actionWithSymmetry(modelChangedAction);
            };

            Fields[nameof(currentSolar)].uiControlEditor.onFieldChanged = delegate (BaseField a, System.Object b) 
            {
                solarModule.modelSelected(currentSolar);
                this.actionWithSymmetry(m =>
                {
                    modelChangedAction(m);
                    m.updateSolarModules();
                });
            };

            Fields[nameof(currentTopTexture)].uiControlEditor.onFieldChanged = topModule.textureSetSelected;
            Fields[nameof(currentCoreTexture)].uiControlEditor.onFieldChanged = coreModule.textureSetSelected;
            Fields[nameof(currentBottomTexture)].uiControlEditor.onFieldChanged = bottomModule.textureSetSelected;

            if (HighLogic.LoadedSceneIsEditor)
            {
                GameEvents.onEditorShipModified.Add(new EventData<ShipConstruct>.OnEvent(onEditorVesselModified));
            }
            updateDragCubes();
        }

        public void Start()
        {
            if (!initializedDefaults)
            {
                updateResourceVolume();
            }
            initializedDefaults = true;
            updateSolarModules();
        }
        
        public void OnDestroy()
        {
            if (HighLogic.LoadedSceneIsEditor)
            {
                GameEvents.onEditorShipModified.Remove(new EventData<ShipConstruct>.OnEvent(onEditorVesselModified));
            }
        }

        public ModifierChangeWhen GetModuleMassChangeWhen() { return ModifierChangeWhen.CONSTANTLY; }

        public ModifierChangeWhen GetModuleCostChangeWhen() { return ModifierChangeWhen.CONSTANTLY; }

        public float GetModuleMass(float defaultMass, ModifierStagingSituation sit)
        {
            if (modifiedMass == 0) { return 0; }
            return -defaultMass + modifiedMass;
        }

        public float GetModuleCost(float defaultCost, ModifierStagingSituation sit)
        {
            if (modifiedCost == 0) { return 0; }
            return -defaultCost + modifiedCost;
        }

        private void onEditorVesselModified(ShipConstruct ship)
        {
            updateGUI();
        }

        public string[] getSectionNames()
        {
            return new string[] { "Top", "Body", "Bottom" };
        }

        public RecoloringData[] getSectionColors(string section)
        {
            if (section == "Top")
            {
                return topModule.customColors;
            }
            else if (section == "Body")
            {
                return coreModule.customColors;
            }
            else if (section == "Bottom")
            {
                return bottomModule.customColors;
            }
            return coreModule.customColors;
        }

        public void setSectionColors(string section, RecoloringData[] colors)
        {
            if (section == "Top")
            {
                topModule.setSectionColors(colors);
            }
            else if (section == "Body")
            {
                coreModule.setSectionColors(colors);
            }
            else if (section == "Bottom")
            {
                bottomModule.setSectionColors(colors);
            }
        }

        //IRecolorable override
        public TextureSet getSectionTexture(string section)
        {
            if (section == "Top")
            {
                return topModule.currentTextureSet;
            }
            else if (section == "Body")
            {
                return coreModule.currentTextureSet;
            }
            else if (section == "Bottom")
            {
                return bottomModule.currentTextureSet;
            }
            return coreModule.currentTextureSet;
        }

        #endregion ENDREGION - Standard KSP Overrides

        #region REGION - Custom Update Methods

        private void initialize(bool start)
        {
            if (initialized) { return; }
            initialized = true;

            topNodeNames = SSTUUtils.parseCSV(topManagedNodes);
            bottomNodeNames = SSTUUtils.parseCSV(bottomManagedNodes);

            ConfigNode node = SSTUConfigNodeUtils.parseConfigNode(configNodeData);

            coreModule = new ModelModule<SingleModelData, SSTUModularServiceModule>(part, this, getRootTransform("MSC-CORE", true), ModelOrientation.TOP, nameof(coreModulePersistentData), nameof(currentCore), nameof(currentCoreTexture));
            coreModule.getSymmetryModule = m => m.coreModule;
            coreModule.setupModelList(SingleModelData.parseModels(node.GetNodes("CORE")));

            topModule = new ModelModule<SingleModelData, SSTUModularServiceModule>(part, this, getRootTransform("MSC-TOP", true), ModelOrientation.TOP, nameof(topModulePersistentData), nameof(currentTop), nameof(currentTopTexture));
            topModule.getSymmetryModule = m => m.topModule;
            topModule.getValidSelections = m => topModule.models.FindAll(s => s.canSwitchTo(part, topNodeNames));

            bottomModule = new ModelModule<SingleModelData, SSTUModularServiceModule>(part, this, getRootTransform("MSC-BOTTOM", true), ModelOrientation.BOTTOM, nameof(bottomModulePersistentData), nameof(currentBottom), nameof(currentBottomTexture));
            bottomModule.getSymmetryModule = m => m.bottomModule;
            bottomModule.getValidSelections = m => bottomModule.models.FindAll(s => s.canSwitchTo(part, bottomNodeNames));

            solarModule = new ModelModule<SolarData, SSTUModularServiceModule>(part, this, getRootTransform("MSC-Solar", true), ModelOrientation.CENTRAL, null, nameof(currentSolar), null);
            solarModule.getSymmetryModule = m => m.solarModule;
            solarModule.setupModelList(SingleModelData.parseModels(node.GetNodes("SOLAR"), m => new SolarData(m)));
            solarModule.getValidSelections = m => solarModule.models.FindAll(s => s.isAvailable(upgradesApplied));

            List<ConfigNode> tops = new List<ConfigNode>();
            List<ConfigNode> bottoms = new List<ConfigNode>();
            ConfigNode[] mNodes = node.GetNodes("CAP");
            ConfigNode mNode;
            int len = mNodes.Length;
            for (int i = 0; i < len; i++)
            {
                mNode = mNodes[i];
                if (mNode.GetBoolValue("useForTop", true)) { tops.Add(mNode); }
                if (mNode.GetBoolValue("useForBottom", true)) { bottoms.Add(mNode); }
            }
            topModule.setupModelList(SingleModelData.parseModels(tops.ToArray()));
            bottomModule.setupModelList(SingleModelData.parseModels(bottoms.ToArray()));

            tops.Clear();
            bottoms.Clear();
            topModule.setupModel();
            coreModule.setupModel();//TODO -- only setup core module if not the prefab part -- else need to add transform updating/fx-updating for RCS and engine modules, as they lack proper handling for transform swapping at runtime
            bottomModule.setupModel();
            solarModule.setupModel();

            updateModulePositions();
            updateMassAndCost();
            updateAttachNodes(false);
            SSTUStockInterop.updatePartHighlighting(part);
        }

        private void updateModulePositions()
        {
            //update for model scale
            topModule.model.updateScaleForDiameter(topDiameter);
            coreModule.model.updateScaleForDiameter(coreDiameter);
            bottomModule.model.updateScaleForDiameter(bottomDiameter);
            solarModule.model.updateScale(1);

            //calc positions
            float yPos = topModule.moduleHeight + (coreModule.moduleHeight * 0.5f);
            float topDockY = yPos;
            yPos -= topModule.moduleHeight;
            float topY = yPos;
            yPos -= coreModule.moduleHeight;
            float coreY = yPos;
            float bottomY = coreY;
            yPos -= bottomModule.moduleHeight;
            float bottomDockY = yPos;

            //update internal ref of position
            topModule.setPosition(topY);
            coreModule.setPosition(coreY);
            solarModule.setPosition(coreY);
            bottomModule.setPosition(bottomY, ModelOrientation.BOTTOM);

            //update actual model positions and scales
            topModule.updateModel();
            coreModule.updateModel();
            bottomModule.updateModel();
            solarModule.updateModel();
        }
        
        private void updateResourceVolume()
        {
            float volume = coreModule.moduleVolume;
            if (useAdapterVolume)
            {
                volume += topModule.moduleVolume;
                volume += bottomModule.moduleVolume;
            }
            SSTUModInterop.onPartFuelVolumeUpdate(part, volume * 1000f);
        }
        
        private void updateMassAndCost()
        {
            modifiedMass = coreModule.moduleMass;
            modifiedMass += solarModule.moduleMass;
            if (useAdapterMass)
            {
                modifiedMass += topModule.moduleMass;
                modifiedMass += bottomModule.moduleMass;
            }

            modifiedCost = coreModule.moduleCost;
            modifiedCost += solarModule.moduleCost;
            if (useAdapterCost)
            {
                modifiedCost += topModule.moduleCost;
                modifiedCost += bottomModule.moduleCost;
            }
        }

        //TODO
        private void updateSolarModules()
        {
            if (!updateSolar)
            {
                return;
            }
            if (solarAnimationControl == null && !string.Equals("none", solarAnimationID))
            {
                SSTUAnimateControlled[] controls = part.GetComponents<SSTUAnimateControlled>();
                int len = controls.Length;
                for (int i = 0; i < len; i++)
                {
                    if (controls[i].animationID == solarAnimationID)
                    {
                        solarAnimationControl = controls[i];
                        break;
                    }
                }
                if (solarAnimationControl == null)
                {
                    MonoBehaviour.print("ERROR: Animation controller was null for ID: " + solarAnimationID);
                    return;
                }
            }

            bool animEnabled = solarModule.model.hasAnimation();
            bool solarEnabled = solarModule.model.panelsEnabled;
            string animName = string.Empty;
            float animSpeed = 1f;

            if (animEnabled)
            {
                ModelAnimationData mad = solarModule.model.modelDefinition.animationData[0];
                animName = mad.animationName;
                animSpeed = mad.speed;
            }
            else
            {
                animName = string.Empty;
                animSpeed = 1f;
            }

            if (solarAnimationControl != null)
            {
                solarAnimationControl.animationName = animName;
                solarAnimationControl.animationSpeed = animSpeed;
                solarAnimationControl.reInitialize();
            }

            SSTUSolarPanelDeployable solar = part.GetComponent<SSTUSolarPanelDeployable>();
            if (solar != null)
            {
                if (solarEnabled)
                {
                    solar.resourceAmount = solarModule.model.energy;
                    solar.pivotTransforms = solarModule.model.pivotNames;
                    solar.rayTransforms = solarModule.model.sunNames;

                    SSTUSolarPanelDeployable.Axis axis = (SSTUSolarPanelDeployable.Axis)Enum.Parse(typeof(SSTUSolarPanelDeployable.Axis), solarModule.model.sunAxis);
                    solar.setSuncatcherAxis(axis);
                    solar.enableModule();
                }
                else
                {
                    solar.disableModule();
                }
            }
        }

        private void updateAttachNodes(bool userInput)
        {
            topModule.model.updateAttachNodes(part, topNodeNames, userInput, ModelOrientation.TOP);
            bottomModule.model.updateAttachNodes(part, bottomNodeNames, userInput, ModelOrientation.BOTTOM);
        }
        
        private void updateGUI()
        {
            topModule.updateSelections();
            bottomModule.updateSelections();
        }

        private void updateDragCubes()
        {
            SSTUModInterop.onPartGeometryUpdate(part, true);
        }

        private Transform getRootTransform(string name, bool recreate)
        {
            Transform root = part.transform.FindRecursive(name);
            if (recreate && root != null)
            {
                GameObject.DestroyImmediate(root.gameObject);
                root = null;
            }
            if (root == null)
            {
                root = new GameObject(name).transform;
            }
            root.NestToParent(part.transform.FindRecursive("model"));
            return root;
        }

        #endregion ENDREGION - Custom Update Methods

    }

    public class ServiceModuleCoreModel : SingleModelData
    {

        //list of available solar panel model definitions
        //each one will have a list of 'positions' relative to the unscaled core model
        //each one will list a minimum 'core scale', below which it is unavailable.
        public ServiceModuleSolarPanelConfiguration[] solarConfigs;
        public ServiceModularRCSPositionConfiguration[] rcsConfigs;

        public ServiceModuleCoreModel(ConfigNode node) : base(node)
        {

        }

        /// <summary>
        /// Return the list of solar panel variants that are currently available to this body model
        /// </summary>
        /// <param name="scale"></param>
        /// <returns></returns>
        public string[] getAvailableSolarVariants(float scale)
        {
            return null;
        }

        /// <summary>
        /// Returns if the input solar panel variant is a valid option at the input scale
        /// </summary>
        /// <param name="name"></param>
        /// <param name="scale"></param>
        /// <returns></returns>
        public bool isValidSolarOption(string name, float scale)
        {
            return false;
        }

    }

    /// <summary>
    /// Wrapper for RCS
    /// </summary>
    public class ServiceModuleRCSModelData : SingleModelData
    {
        public GameObject[] models;
        public string thrustTransformName;
        public bool dummyModel = false;
        public float currentHorizontalPosition;
        public float modelRotation = 0;
        public float modelHorizontalZOffset = 0;
        public float modelHorizontalXOffset = 0;
        public float modelVerticalOffset = 0;

        public float mountVerticalRotation = 0;
        public float mountHorizontalRotation = 0;

        public ServiceModuleRCSModelData(ConfigNode node) : base(node)
        {
            dummyModel = node.GetBoolValue("dummyModel");
            modelRotation = node.GetFloatValue("modelRotation");
            modelHorizontalZOffset = node.GetFloatValue("modelHorizontalZOffset");
            modelHorizontalXOffset = node.GetFloatValue("modelHorizontalXOffset");
            modelVerticalOffset = node.GetFloatValue("modelVerticalOffset");
            thrustTransformName = modelDefinition.configNode.GetStringValue("thrustTransformName");
        }

        public override void setupModel(Transform parent, ModelOrientation orientation)
        {
            model = new GameObject(modelDefinition.name);
            model.transform.NestToParent(parent);
            if (models != null) { destroyCurrentModel(); }
            models = new GameObject[4];
            for (int i = 0; i < 4; i++)
            {
                models[i] = SSTUUtils.cloneModel(modelDefinition.modelName);
            }
            foreach (GameObject go in models)
            {
                go.transform.NestToParent(parent);
            }
        }

        public override void updateModel()
        {
            if (models != null)
            {
                float rotation = 0;
                float posX = 0, posZ = 0, posY = 0;
                float scale = 1;
                float length = 0;
                for (int i = 0; i < 4; i++)
                {
                    rotation = (float)(i * 90) + mountVerticalRotation;
                    scale = currentDiameterScale;
                    length = currentHorizontalPosition + (scale * modelHorizontalZOffset);
                    posX = (float)Math.Sin(SSTUUtils.toRadians(rotation)) * length;
                    posZ = (float)Math.Cos(SSTUUtils.toRadians(rotation)) * length;
                    posY = currentVerticalPosition + (scale * modelVerticalOffset);
                    models[i].transform.localScale = new Vector3(currentDiameterScale, currentHeightScale, currentDiameterScale);
                    models[i].transform.localPosition = new Vector3(posX, posY, posZ);
                    models[i].transform.localRotation = Quaternion.AngleAxis(rotation + 90f, new Vector3(0, 1, 0));
                    models[i].transform.Rotate(new Vector3(0, 0, 1), mountHorizontalRotation, Space.Self);
                }
            }
        }

        public override void destroyCurrentModel()
        {
            if (models == null) { return; }
            int len = models.Length;
            for (int i = 0; i < len; i++)
            {
                if (models[i] == null) { continue; }
                models[i].transform.parent = null;
                GameObject.Destroy(models[i]);
                models[i] = null;
            }
            models = null;
        }

        public GameObject[] createThrustTransforms(string name, Transform parent)
        {
            MonoBehaviour.print("Creating new thrust transforms");
            if (dummyModel)
            {
                GameObject[] dumArr = new GameObject[1];
                dumArr[0] = new GameObject(name);
                dumArr[0].transform.NestToParent(parent);
                return dumArr;
            }
            int len = 4, len2;
            List<GameObject> goList = new List<GameObject>();
            Transform[] trs;
            GameObject go;
            for (int i = 0; i < len; i++)
            {
                trs = models[i].transform.FindChildren(thrustTransformName);
                len2 = trs.Length;
                for (int k = 0; k < len2; k++)
                {
                    go = new GameObject(name);
                    go.transform.NestToParent(parent);
                    goList.Add(go);
                }
            }
            return goList.ToArray();
        }

        public void updateThrustTransformPositions(GameObject[] gos)
        {
            MonoBehaviour.print("Updating transform positions");
            if (dummyModel) { return; }
            Transform[] trs;
            int len;
            GameObject go;
            int index = 0;
            int goLen = gos.Length;
            for (int i = 0; i < 4; i++)
            {
                trs = models[i].transform.FindChildren(thrustTransformName);
                len = trs.Length;
                for (int k = 0; k < len && index < goLen; k++, index++)
                {
                    go = gos[index];
                    go.transform.position = trs[k].position;
                    go.transform.rotation = trs[k].rotation;
                }
            }
        }

    }

    //AKA I fucking hate the bullshit that other people request of me, they really need to bow down and learn to suck it....
    // or maybe just learn to FUCKING DOIT THEIR GODDAMN LAZY ASS SELVES
    public class ServiceModuleSolarPanelConfiguration
    {

    }

    //AKA THISISDUMB2TOO
    public class ServiceModularRCSPositionConfiguration
    {

    }

}