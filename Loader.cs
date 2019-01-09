using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace DeadfireMapTool
{
    public class Loader
    {
        private static GameObject hostObject;

        public static void Load()
        {
            hostObject = new GameObject("_HostGameObject_");
            hostObject.AddComponent<DeadfireMapToolBehaviour>();
            hostObject.AddComponent<ToggleTool>();

            UnityEngine.Object.DontDestroyOnLoad(hostObject);
        }

        public static void Unload()
        {
            UnityEngine.Object.Destroy(hostObject);
        }
    }

    public static class WinAPI
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr MessageBox(int hWnd, string text, string caption, uint type);
    }
}
