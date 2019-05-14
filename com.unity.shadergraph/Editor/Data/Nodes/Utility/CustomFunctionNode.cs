﻿using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEditor.Graphing;
using UnityEditor.Rendering;
using UnityEngine.UIElements;
using UnityEditor.ShaderGraph.Drawing;

namespace UnityEditor.ShaderGraph
{
    [Title("Utility", "Custom Function")]
    class CustomFunctionNode : AbstractMaterialNode, IGeneratesBodyCode, IGeneratesFunction, IHasSettings
    {
        static string[] s_ValidExtensions = { ".hlsl", ".cginc" };
        static string s_NoFunctionNameSet = "A Custom Function Node requires a function name to be set.";
        static string s_NoFunctionBodySet = "String mode requires a function body to be set.";
        static string s_NoFileSelected = "File mode requires a Source file to be selected.";
        static string s_InvalidFileType = "Source file is not a valid file type. Valid file extensions are .hlsl and .cginc";
        static string s_MissingOutputSlot = "A Custom Function Node must have at least one output slot";

        public CustomFunctionNode()
        {
            name = "Custom Function";
        }

        public override bool hasPreview => true;

        [SerializeField]
        private HlslSourceType m_SourceType = HlslSourceType.File;

        public HlslSourceType sourceType
        {
            get => m_SourceType;
            set => m_SourceType = value;
        }

        [SerializeField]
        private string m_FunctionName = m_DefaultFunctionName;

        private static string m_DefaultFunctionName = "Enter function name here...";

        public string functionName
        {
            get => m_FunctionName;
            set => m_FunctionName = value;
        }

        public static string defaultFunctionName => m_DefaultFunctionName;

        [SerializeField]
        private string m_FunctionSource;

        private static string m_DefaultFunctionSource = "Enter function source file path here...";

        public string functionSource
        {
            get => m_FunctionSource;
            set => m_FunctionSource = value;
        }

        [SerializeField]
        private string m_FunctionBody = m_DefaultFunctionBody;

        private static string m_DefaultFunctionBody = "Enter function body here...";

        public string functionBody
        {
            get => m_FunctionBody;
            set => m_FunctionBody = value;
        }

        public static string defaultFunctionBody => m_DefaultFunctionBody;

        public void GenerateNodeCode(ShaderGenerator visitor, GraphContext graphContext, GenerationMode generationMode)
        {
            List<MaterialSlot> slots = new List<MaterialSlot>();
            GetOutputSlots<MaterialSlot>(slots);

            if(!IsValidFunction())
            {
                if(generationMode == GenerationMode.Preview && slots.Count != 0)
                {
                    slots.OrderBy(s => s.id);
                    visitor.AddShaderChunk(string.Format("{0} {1};",
                        NodeUtils.ConvertConcreteSlotValueTypeToString(precision, slots[0].concreteValueType),
                        GetVariableNameForSlot(slots[0].id)));
                }
                return;
            }

            foreach (var argument in slots)
                visitor.AddShaderChunk(string.Format("{0} {1};",
                    NodeUtils.ConvertConcreteSlotValueTypeToString(precision, argument.concreteValueType),
                    GetVariableNameForSlot(argument.id)));

            string call = string.Format("{0}_{1}(", functionName, precision);
            bool first = true;

            slots.Clear();
            GetInputSlots<MaterialSlot>(slots);
            foreach (var argument in slots)
            {
                if (!first)
                    call += ", ";
                first = false;
                call += SlotInputValue(argument, generationMode);
            }

            slots.Clear();
            GetOutputSlots<MaterialSlot>(slots);
            foreach (var argument in slots)
            {
                if (!first)
                    call += ", ";
                first = false;
                call += GetVariableNameForSlot(argument.id);
            }
            call += ");";
            visitor.AddShaderChunk(call, true);
        }

        public void GenerateNodeFunction(FunctionRegistry registry, GraphContext graphContext, GenerationMode generationMode)
        {
            if(!IsValidFunction())
                return;

            switch (sourceType)
            {
                case HlslSourceType.File:
                    registry.ProvideFunction(functionSource, builder =>
                    {
                        string path = AssetDatabase.GUIDToAssetPath(functionSource);

                        // This is required for upgrading without console errors
                        if(string.IsNullOrEmpty(path))
                            path = functionSource;

                        builder.AppendLine($"#include \"{path}\"");
                    });
                    break;
                case HlslSourceType.String:
                    registry.ProvideFunction(functionName, builder =>
                    {
                        builder.AppendLine(GetFunctionHeader());
                        using (builder.BlockScope())
                        {
                            builder.AppendLines(functionBody);
                        }
                    });
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private string GetFunctionHeader()
        {
            string header = string.Format("void {0}_{1}(", functionName, precision);
            var first = true;
            List<MaterialSlot> slots = new List<MaterialSlot>();

            GetInputSlots<MaterialSlot>(slots);
            foreach (var argument in slots)
            {
                if (!first)
                    header += ", ";
                first = false;
                header += string.Format("{0} {1}", argument.concreteValueType.ToString(precision), argument.shaderOutputName);
            }

            slots.Clear();
            GetOutputSlots<MaterialSlot>(slots);
            foreach (var argument in slots)
            {
                if (!first)
                    header += ", ";
                first = false;
                header += string.Format("out {0} {1}", argument.concreteValueType.ToString(precision), argument.shaderOutputName);
            }
            header += ")";
            return header;
        }

        private string SlotInputValue(MaterialSlot port, GenerationMode generationMode)
        {
            IEdge[] edges = port.owner.owner.GetEdges(port.slotReference).ToArray();
            if (edges.Any())
            {
                var fromSocketRef = edges[0].outputSlot;
                var fromNode = owner.GetNodeFromGuid<AbstractMaterialNode>(fromSocketRef.nodeGuid);
                if (fromNode == null)
                    return string.Empty;

                var slot = fromNode.FindOutputSlot<MaterialSlot>(fromSocketRef.slotId);
                if (slot == null)
                    return string.Empty;

                return ShaderGenerator.AdaptNodeOutput(fromNode, slot.id, port.concreteValueType);
            }

            return port.GetDefaultValue(generationMode);
        }

        private bool IsValidFunction()
        {
            bool validFunctionName = !string.IsNullOrEmpty(functionName) && functionName != m_DefaultFunctionName;

            if(sourceType == HlslSourceType.String)
            {
                bool validFunctionBody = !string.IsNullOrEmpty(functionBody) && functionBody != m_DefaultFunctionBody;
                return validFunctionName & validFunctionBody;
            }
            else
            {
                if(!validFunctionName || string.IsNullOrEmpty(functionSource) || functionSource == m_DefaultFunctionSource)
                    return false;

                string path = AssetDatabase.GUIDToAssetPath(functionSource);
                if(string.IsNullOrEmpty(path))
                    path = functionSource;

                string extension = path.Substring(path.LastIndexOf('.'));
                return s_ValidExtensions.Contains(extension);
            }
        }

        void ValidateSlotName()
        {
            List<MaterialSlot> slots = new List<MaterialSlot>();
            GetSlots(slots);

            foreach (var slot in slots)
            {
                var error = NodeUtils.ValidateSlotName(slot.RawDisplayName(), out string errorMessage);
                if (error)
                {
                    owner.AddValidationError(tempId, errorMessage);
                    break;
                }
            }
        }

        public override void ValidateNode()
        {
            if (!this.GetOutputSlots<MaterialSlot>().Any())
            {
                owner.AddValidationError(tempId, s_MissingOutputSlot, ShaderCompilerMessageSeverity.Warning);
            }

            if(functionName == m_DefaultFunctionName)
            {
                owner.AddValidationError(tempId, s_NoFunctionNameSet, ShaderCompilerMessageSeverity.Warning);
            }

            if(sourceType == HlslSourceType.String)
            {
                if(functionBody == m_DefaultFunctionBody)
                {
                    owner.AddValidationError(tempId, s_NoFunctionBodySet, ShaderCompilerMessageSeverity.Warning);
                }
            }
            else
            {
                if(string.IsNullOrEmpty(functionSource))
                {
                    owner.AddValidationError(tempId, s_NoFileSelected, ShaderCompilerMessageSeverity.Warning);
                }

                string path = AssetDatabase.GUIDToAssetPath(functionSource);
                if(!string.IsNullOrEmpty(path))
                {
                    string extension = path.Substring(path.LastIndexOf('.'));
                    if(!s_ValidExtensions.Contains(extension))
                    {
                        owner.AddValidationError(tempId, s_InvalidFileType, ShaderCompilerMessageSeverity.Error);
                    }
                }
            }
            ValidateSlotName();

            base.ValidateNode();
        }

        public override void GetSourceAssetDependencies(List<string> paths)
        {
            base.GetSourceAssetDependencies(paths);
            if (sourceType == HlslSourceType.File && IsValidFunction())
            {
                paths.Add(functionSource);
                foreach (var dependencyPath in AssetDatabase.GetDependencies(functionSource))
                    paths.Add(dependencyPath);
            }
        }

        public VisualElement CreateSettingsElement()
        {
            PropertySheet ps = new PropertySheet();
            ps.Add(new ReorderableSlotListView(this, SlotType.Input));
            ps.Add(new ReorderableSlotListView(this, SlotType.Output));
            ps.Add(new HlslFunctionView(this));
            return ps;
        }
    }
}
