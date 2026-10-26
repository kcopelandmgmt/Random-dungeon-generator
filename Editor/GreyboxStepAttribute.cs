using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace DungeonGen.Unity.Editor
{
    /// <summary>
    /// Marks a <c>public static void Step(GreyboxContext ctx)</c> method that <see cref="GreyboxSceneSetup.Build"/> calls after
    /// the base scene exists, in ascending <see cref="Order"/>. Each feature (models, audio, HUD, menus) adds its own step in its
    /// own file, so no two pieces of work edit the setup itself.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class GreyboxStepAttribute : Attribute
    {
        public int Order { get; }
        public GreyboxStepAttribute(int order = 0) { Order = order; }
    }

    /// <summary>Everything the base setup made, for the steps to wire into.</summary>
    public sealed class GreyboxContext
    {
        public GameObject Root;
        public DungeonBuilder Builder;
        public DungeonMatchBootstrap Bootstrap;
        public ServerSimulation Server;
        public ClientWorldView World;
        public PlayerHud Hud;
        public CompassHud Compass;
        public MiniMapHud MiniMap;
        public AbilityEffects Effects;
        public TopDownCamera Camera;
        public PracticeMode Practice;
        public MonoBehaviour Controller;   // LocalPlayerController when the Input System is installed
    }

    internal static class GreyboxSteps
    {
        public static void RunAll(GreyboxContext ctx)
        {
            var steps = new List<(int order, MethodInfo m)>();
            foreach (var t in typeof(GreyboxSteps).Assembly.GetTypes())
                foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                {
                    var a = m.GetCustomAttribute<GreyboxStepAttribute>();
                    if (a != null) steps.Add((a.Order, m));
                }
            foreach (var (order, m) in steps.OrderBy(s => s.order).ThenBy(s => s.m.DeclaringType.FullName))
            {
                try { m.Invoke(null, new object[] { ctx }); Debug.Log($"Dungeon Gen: greybox step {m.DeclaringType.Name}.{m.Name} done."); }
                catch (Exception ex) { Debug.LogError($"Dungeon Gen: greybox step {m.DeclaringType.Name}.{m.Name} failed: {(ex.InnerException ?? ex)}"); }
            }
        }
    }
}
