using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using DunGen;
using HarmonyLib;
using UnityEngine;

namespace LethalFantasyMapScaler.Patches
{
    // DunGen bug: DungeonGenerator.InnerGenerate has a retry-limit guard:
    //
    //   if (retryCount >= MaxAttemptCount && Application.isEditor) { ... fail ... }
    //
    // The && Application.isEditor means the limit NEVER fires in a real build.
    // Each failed tile placement calls Wait(InnerGenerate(true)) recursively, and
    // InnerGenerate calls Wait(GenerateMainPath()), creating infinite mutual
    // recursion through Unity's coroutine StartCoroutine stack → StackOverflow.
    //
    // Fix: replace the Application.get_isEditor call with ldc.i4.1 (constant true)
    // so the retry limit fires correctly in builds.
    [HarmonyPatch]
    internal static class InnerGenerateRetryLimitPatch
    {
        static MethodBase TargetMethod()
        {
            Type stateMachine = typeof(DungeonGenerator)
                .GetNestedTypes(BindingFlags.NonPublic)
                .FirstOrDefault(t => t.Name.Contains("InnerGenerate"));

            if (stateMachine == null)
            {
                Plugin.Log.LogWarning(
                    "[MapScaler] Could not find InnerGenerate state machine — " +
                    "retry-limit fix NOT applied. Stack overflow may occur on large maps.");
                return null;
            }

            MethodInfo moveNext = stateMachine.GetMethod(
                "MoveNext", BindingFlags.Instance | BindingFlags.NonPublic);

            if (moveNext == null)
                Plugin.Log.LogWarning("[MapScaler] MoveNext not found on InnerGenerate state machine.");

            return moveNext;
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo isEditorGetter = typeof(UnityEngine.Application)
                .GetProperty("isEditor", BindingFlags.Public | BindingFlags.Static)
                ?.GetGetMethod();

            if (isEditorGetter == null)
            {
                Plugin.Log.LogWarning("[MapScaler] Could not find Application.isEditor — retry-limit fix NOT applied.");
                foreach (var i in instructions) yield return i;
                yield break;
            }

            bool patched = false;
            foreach (var instr in instructions)
            {
                // Replace: call bool Application::get_isEditor()
                // With:    ldc.i4.1  (constant true — limit always applies in builds)
                if (!patched
                    && instr.opcode == OpCodes.Call
                    && instr.operand is MethodInfo mi
                    && mi == isEditorGetter)
                {
                    yield return new CodeInstruction(OpCodes.Ldc_I4_1);
                    patched = true;
                    Plugin.Log.LogDebug("[MapScaler] InnerGenerate retry limit: Application.isEditor replaced with true.");
                    continue;
                }
                yield return instr;
            }

            if (!patched)
                Plugin.Log.LogWarning(
                    "[MapScaler] InnerGenerate transpiler: Application.isEditor not found — " +
                    "retry-limit fix NOT applied. Stack overflow may occur on large maps.");
        }
    }

    // Also patch GenerateBranchPaths if it has the same guard.
    // (Verify: search for the same pattern in its state machine.)
    [HarmonyPatch]
    internal static class BranchGenerateRetryLimitPatch
    {
        static MethodBase TargetMethod()
        {
            Type stateMachine = typeof(DungeonGenerator)
                .GetNestedTypes(BindingFlags.NonPublic)
                .FirstOrDefault(t => t.Name.Contains("GenerateBranchPaths"));

            if (stateMachine == null) return null; // not found, skip silently

            return stateMachine.GetMethod("MoveNext", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo isEditorGetter = typeof(UnityEngine.Application)
                .GetProperty("isEditor", BindingFlags.Public | BindingFlags.Static)
                ?.GetGetMethod();

            if (isEditorGetter == null)
            {
                foreach (var i in instructions) yield return i;
                yield break;
            }

            bool patched = false;
            foreach (var instr in instructions)
            {
                if (!patched
                    && instr.opcode == OpCodes.Call
                    && instr.operand is MethodInfo mi
                    && mi == isEditorGetter)
                {
                    yield return new CodeInstruction(OpCodes.Ldc_I4_1);
                    patched = true;
                    Plugin.Log.LogDebug("[MapScaler] GenerateBranchPaths retry limit: Application.isEditor replaced with true.");
                    continue;
                }
                yield return instr;
            }
        }
    }
}
