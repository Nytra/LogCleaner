using HarmonyLib;
using ResoniteModLoader;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Collections.Generic;
using FrooxEngine;
using System.Reflection.Emit;
using Elements.Core;
using SkyFrost.Base;
using System.Runtime.CompilerServices;

namespace LogCleaner
{
    public class LogCleaner : ResoniteMod
    {
        public override string Name => "LogCleaner";
        public override string Author => "art0007i";
        public override string Version => "1.1.0";
        public override string Link => "https://github.com/art0007i/LogCleaner/";

        public static HashSet<MethodInfo> logMethods = new HashSet<MethodInfo>
        {
            typeof(UniLog).GetMethod("Log", new Type[] { typeof(string), typeof(bool) }),
            typeof(UniLog).GetMethod("Log", new Type[] { typeof(object), typeof(bool) }),
            typeof(UniLog).GetMethod("Warning"),
            typeof(UniLog).GetMethod("Error")
        };

        public static Type[] ctxMenuTypes =
        {
            typeof(AssetToolReplacementMode),
            typeof(DevTool.Selection),
            typeof(DevTool.Interaction),
            typeof(LightTool.Mode),
            typeof(ShadowType),
            typeof(MeterTool.Mode),
            typeof(AssetToolReplacementMode),
            typeof(bool),
            typeof(CameraControlTool.CameraMode),
            typeof(Glue.Mode),
            typeof(MicrophoneTool.RecordFormat),
            typeof(MicrophoneTool.RecordMode),
            typeof(MicrophoneTool.DataSource),
            typeof(VolumePlaneMode),
            typeof(ShadowCastMode)
        };

        // functions in here will still print messages, but not stack traces
        public static HashSet<MethodInfo> removeStackTrace = new HashSet<MethodInfo>
        {
            // This happens whenever you change name or description of a world (often triggered without interaction)
            AccessTools.Method(typeof(WorldConfiguration), "FieldChanged"),
        };

        // functions in here will be completely silenced
        public static HashSet<MethodInfo> removeLog = new HashSet<MethodInfo>
        {
            // Very spammy and doesn't seem like it has any useful info
            AccessTools.Method(typeof(ContactData), "UpdateStatus"),
            AccessTools.Method(typeof(ContactData), "ClearExpired"),
            AccessTools.Method(typeof(UserStatusManager), "SendStatusToUser"),
            FindAsyncBody(AccessTools.Method(typeof(AppHub), "BroadcastStatus")),
        };

        public static MethodInfo FindAsyncBody (MethodInfo mi)
        {
            AsyncStateMachineAttribute asyncAttribute = (AsyncStateMachineAttribute)mi.GetCustomAttribute(typeof(AsyncStateMachineAttribute));
            Type asyncStateMachineType = asyncAttribute.StateMachineType;
            return AccessTools.Method(asyncStateMachineType, nameof(IAsyncStateMachine.MoveNext));
        }

        public override void OnEngineInit()
        {
            Harmony harmony = new Harmony("me.art0007i.LogCleaner");

            Debug("Patching ctx menu funcs");
            // Patch context menu stuffs
            var GenericToPatch = typeof(RadialMenuItemExtensions).GetMethod(nameof(RadialMenuItemExtensions.AttachOptionDescriptionDriver));
            var patcherMethod = new HarmonyMethod(AccessTools.Method(typeof(ContextMenuOpenPatch), nameof(ContextMenuOpenPatch.Transpiler)));
            foreach (var item in ctxMenuTypes)
            {
                var toPatch = GenericToPatch.MakeGenericMethod(item);
                harmony.Patch(toPatch, transpiler: patcherMethod);
            }

            Debug("Patching stack trace funcs");
            // Remove stack traces
            MethodInfo transpiler = typeof(StackTraceFixerPatch).GetMethod(nameof(StackTraceFixerPatch.Transpiler));
            foreach (MethodInfo method in removeStackTrace)
            {
                Debug("Patching method " + method);
                try
                {
                    harmony.Patch(method, transpiler: new HarmonyMethod(transpiler));
                }
                catch (Exception e)
                {
                    Debug("  error in patch for " + method);
                    Debug(e);
                }
            }

            Debug("Patching remove log funcs");
            // Remove log methods
            MethodInfo transpiler2 = typeof(RemoveLogMethodPatch).GetMethod(nameof(RemoveLogMethodPatch.Transpiler));
            foreach (MethodInfo method in removeLog)
            {
                Debug("Patching method " + method);
                try
                {
                    harmony.Patch(method, transpiler: new HarmonyMethod(transpiler2));
                }
                catch (Exception e)
                {
                    Debug("  error in patch for " + method);
                    Debug(e);
                }
            }
        }
        class StackTraceFixerPatch
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes)
            {
                List<CodeInstruction> instructions = new List<CodeInstruction>(codes);
                for (int i = 0; i < instructions.Count - 1; i++)
                {
                    // detect integer one (aka true) on stack followed by a call
                    if (instructions[i].opcode == OpCodes.Ldc_I4_1
                        && instructions[i + 1].opcode == OpCodes.Call)
                    {
                        // Is it a UniLog method?
                        if (logMethods.Contains(instructions[i + 1].operand))
                        {
                            // Make it not print a stack trace
                            instructions[i].opcode = OpCodes.Ldc_I4_0;

                            Debug($"Found a {instructions[i + 1].operand}");
                        }
                    }
                }
                Debug("Done!");
                return instructions;
            }
        }

        class ContextMenuOpenPatch
        {
            public static void SafeSetTarget(ISyncRef driver, ISyncRef target)
            {
                if (!target.IsLinked) driver.Target = target;
            }

            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes)
            {
                foreach (var code in codes)
                {
                    if(code.operand is MethodInfo mf && mf.Name == "set_Target")
                    {
                        yield return new(OpCodes.Call, typeof(ContextMenuOpenPatch).GetMethod(nameof(SafeSetTarget)));
                    }
                    else
                    {
                        yield return code;
                    }
                }
            }
        }

        class RemoveLogMethodPatch
        {
            public static void FakeLog(string message, bool stackTrace) { }
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> codes)
            {
                var patching = typeof(RemoveLogMethodPatch).GetMethod(nameof(FakeLog));
                foreach (var code in codes)
                {
                    if (logMethods.Contains(code.operand))
                    {
                        yield return new(OpCodes.Call, patching);
                    }
                    else
                    {
                        yield return code;
                    }
                }
            }
        }
    }
}