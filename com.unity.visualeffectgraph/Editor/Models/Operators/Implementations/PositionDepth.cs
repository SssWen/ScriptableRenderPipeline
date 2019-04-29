using System;
using System.Linq;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.Experimental.VFX;

namespace UnityEditor.VFX.Operator
{
    [VFXInfo(category = "Sampling")]
    class PositionDepth : VFXOperator
    {
        public enum PositionMode
        {
            Random,
            Sequential,
            Custom,
        }

        public enum CullMode
        {
            None,
            FarPlane,
            Range,
        }

        public class InputProperties
        {
            [Tooltip("Sets a scale multiplier to the depth value. Values above 1 will push particles further back, values lower than 1 will pull them closer to the screen.")]
            public float ZMultiplier = 1.0f;
        }

        public class SequentialInputProperties
        {
            [Tooltip("Sets the space between sequentially-placed particles. Lower numbers lead to a denser placement.")]
            public uint GridStep = 1;
        }

        public class CustomInputProperties
        {
            [Range(0.0f, 1.0f)]
            public Vector2 UVSpawn;
        }

        public class RangeInputProperties
        {
            [Range(0.0f, 1.0f)]
            public Vector2 DepthRange = new Vector2(0.0f, 1.0f);
        }

        [VFXSetting]
        [Tooltip("Specifies which Camera to use to project particles onto its depth. Can use the camera tagged 'Main', or a custom camera.")]
        public Block.CameraMode camera;

        [VFXSetting]
        [Tooltip("Specifies how particles are positioned on the screen. They can be placed sequentially in an even grid, randomly, or with a custom UV position.")]
        public PositionMode mode;

        [VFXSetting]
        [Tooltip("Specifies how to determine whether the particle should be alive. A particle can be culled when it is projected on the far camera plane, between a specific range, or culling can be disabled.")]
        public CullMode cullMode;

        [VFXSetting(VFXSettingAttribute.VisibleFlags.InInspector)]
        public bool inheritSceneColor = false;

        public class OutputPropertiesCommon
        {
            [Tooltip("The particle position projected on the depth buffer of the selected Camera.")]
            public Vector3 position = Vector3.zero;
        }

        public class OutputPropertiesCull
        {
            [Tooltip("Determines whether the particle is alive or culled by the Cull Mode settings.")]
            public bool isAlive = true;
        }

        public class OutputPropertiesColor
        {
            [Tooltip("The color of the particle, derived from the color buffer of the selected Camera.")]
            public Color color = Color.black;
        }

        public override string name
        {
            get
            {
                return "Position (Depth)";
            }
        }

        protected override IEnumerable<VFXPropertyWithValue> inputProperties
        {
            get
            {
                var inputs = Enumerable.Empty<VFXPropertyWithValue>();
                if (camera == Block.CameraMode.Custom)
                    inputs = inputs.Concat(PropertiesFromType(typeof(Block.CameraHelper.CameraProperties)));
                inputs = inputs.Concat(PropertiesFromType("InputProperties"));
                if (mode == PositionMode.Sequential)
                    inputs = inputs.Concat(PropertiesFromType("SequentialInputProperties"));
                else if (mode == PositionMode.Custom)
                    inputs = inputs.Concat(PropertiesFromType("CustomInputProperties"));
                if (cullMode == CullMode.Range)
                    inputs = inputs.Concat(PropertiesFromType("RangeInputProperties"));
                return inputs;
            }
        }

        protected override IEnumerable<VFXPropertyWithValue> outputProperties
        {
            get
            {
                IEnumerable<VFXPropertyWithValue> properties = PropertiesFromType(nameof(OutputPropertiesCommon));

                if (inheritSceneColor)
                    properties = properties.Concat(PropertiesFromType(nameof(OutputPropertiesColor)));

                if (cullMode != CullMode.None)
                    properties = properties.Concat(PropertiesFromType(nameof(OutputPropertiesCull)));
                
                return properties;
            }
        }

        protected override VFXExpression[] BuildExpression(VFXExpression[] inputExpression)
        {
            // List to gather all output expressions as their number can vary
            List<VFXExpression> outputs = new List<VFXExpression>();

            // Camera expressions
            VFXExpression Camera_depthBuffer = new VFXExpressionGetBufferFromMainCamera(VFXCameraBufferTypes.Depth);
            VFXExpression CamPixDim = new VFXExpressionExtractPixelDimensionsFromMainCamera();

            // Camera matrices
            var expressions = Block.CameraHelper.AddCameraExpressions(GetExpressionsFromSlots(this), camera);
            Block.CameraMatricesExpressions camMatrices = Block.CameraHelper.GetMatricesExpressions(expressions);

            // Set uvs
            VFXExpression uv = VFXValue.Constant<Vector2>();

            switch (mode)
            {
                case PositionMode.Random:
                    // Random UVs
                    uv = new VFXExpressionCombine(VFXOperatorUtility.FixedRandom(0, true), VFXOperatorUtility.FixedRandom(1, true));
                    break;

                case PositionMode.Sequential:
                    // Pixel perfect spawn
                    VFXExpression gridStep = inputExpression[inputSlots.IndexOf(inputSlots.First(o => o.name == "GridStep"))];

                    VFXExpression sSizeX = new VFXExpressionCastFloatToUint(CamPixDim.x / new VFXExpressionCastUintToFloat(gridStep));
                    VFXExpression sSizeY = new VFXExpressionCastFloatToUint(CamPixDim.y / new VFXExpressionCastUintToFloat(gridStep));

                    VFXExpression nbPixels = sSizeX * sSizeY;
                    VFXExpression particleID = new VFXAttributeExpression(VFXAttribute.ParticleId);
                    VFXExpression id = VFXOperatorUtility.Modulo(particleID, nbPixels);

                    VFXExpression shift = new VFXExpressionBitwiseRightShift(gridStep, VFXValue.Constant<uint>(1));

                    VFXExpression U = VFXOperatorUtility.Modulo(id, sSizeX) * gridStep + shift;
                    VFXExpression V = id / sSizeX * gridStep + shift;

                    VFXExpression ids = new VFXExpressionCombine(new VFXExpressionCastUintToFloat(U), new VFXExpressionCastUintToFloat(V));

                    uv = new VFXExpressionDivide(ids + VFXOperatorUtility.CastFloat(VFXValue.Constant(0.5f), VFXValueType.Float2), CamPixDim);
                    break;

                case PositionMode.Custom:
                    // Custom UVs
                    uv = inputExpression[inputSlots.IndexOf(inputSlots.First(o => o.name == "UVSpawn"))];
                    break;
            }

            VFXExpression projpos = uv * VFXValue.Constant<Vector2>(new Vector2(2f, 2f)) - VFXValue.Constant<Vector2>(Vector2.one);
            VFXExpression uvs = new VFXExpressionCombine(uv.x * CamPixDim.x, uv.y * CamPixDim.y, VFXValue.Constant(0f));

            // Get depth
            VFXExpression depth = new VFXExpressionExtractComponent(new VFXExpressionLoadTexture2D(Camera_depthBuffer, uvs), 0);
            VFXExpression inverseDepth = VFXOperatorUtility.OneExpression[depth.valueType] - depth;

            VFXExpression isAlive = VFXValue.Constant(true);

            switch (cullMode)
            {
                case CullMode.None:
                    // do nothing
                    break;

                case CullMode.Range:

                    VFXExpression depthRange = inputExpression[inputSlots.IndexOf(inputSlots.First(o => o.name == "DepthRange"))];

                    VFXExpression nearRangeCheck = new VFXExpressionCondition(VFXCondition.Less, depth, depthRange.x);
                    VFXExpression farRangeCheck = new VFXExpressionCondition(VFXCondition.Greater, depth, depthRange.y);
                    VFXExpression logicOr = new VFXExpressionLogicalOr(nearRangeCheck, farRangeCheck);
                    isAlive = new VFXExpressionBranch(logicOr, VFXValue.Constant(false), VFXValue.Constant(true));
                    break;

                case CullMode.FarPlane:
                    VFXExpression farPlaneCheck = new VFXExpressionCondition(VFXCondition.GreaterOrEqual, depth, VFXValue.Constant(1f) - VFXValue.Constant(Mathf.Epsilon));
                    isAlive = new VFXExpressionBranch(farPlaneCheck, VFXValue.Constant(false), VFXValue.Constant(true));
                    break;
            }

            VFXExpression clipPos = new VFXExpressionCombine(projpos.x, projpos.y,
                inverseDepth * inputExpression[inputSlots.IndexOf(inputSlots.First(o => o.name == "ZMultiplier"))] * VFXValue.Constant(2f) - VFXValue.Constant(1f),
                VFXValue.Constant(1f)
                );

            VFXExpression clipToVFX = new VFXExpressionTransformMatrix(camMatrices.ViewToVFX.exp, camMatrices.ClipToView.exp);
            VFXExpression vfxPos = new VFXExpressionTransformVector4(clipToVFX, clipPos);
            VFXExpression position = new VFXExpressionCombine(vfxPos.x, vfxPos.y, vfxPos.z) / VFXOperatorUtility.CastFloat(vfxPos.w, VFXValueType.Float3);

            VFXExpression color = VFXValue.Constant<Vector4>();

            if (inheritSceneColor)
            {
                VFXExpression Camera_colorBuffer = new VFXExpressionGetBufferFromMainCamera(VFXCameraBufferTypes.Color);
                VFXExpression tempColor = new VFXExpressionLoadTexture2D(Camera_colorBuffer, uvs);
                color = new VFXExpressionCombine(tempColor.x, tempColor.y, tempColor.z, VFXValue.Constant(1.0f));
            }

            // Add expressions in the right output order 
            outputs.Add(position);

            if (inheritSceneColor)
                outputs.Add(color);

            if (cullMode != CullMode.None)
                outputs.Add(isAlive);

            return outputs.ToArray();

        }
    }
}
