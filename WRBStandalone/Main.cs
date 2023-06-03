using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using R2API.Utils;
using RoR2;
using RoR2.UI;
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;

namespace WRBStandalone
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    [BepInDependency("BALLS.WellRoundedBalance", BepInDependency.DependencyFlags.SoftDependency)]
    public class Main : BaseUnityPlugin
    {
        public const string PluginGUID = PluginAuthor + "." + PluginName;
        public const string PluginAuthor = "zzz.prodzpod";
        public const string PluginName = "WRBStandalone";
        public const string PluginVersion = "1.0.2";
        public static ManualLogSource Log;
        public static PluginInfo pluginInfo;

        public static MPInput input;
        public static MPButton button;

        public static bool WRBInstalled;
        public static ConfigFile Config;
        public static ConfigEntry<string> lookup;
        public static string lookupGUID;
        public static ConfigEntry<string> installPath;

        public static string modname;
        public void Awake()
        {
            pluginInfo = Info;
            Log = Logger;
            Config = new(System.IO.Path.Combine(Paths.ConfigPath, PluginGUID + ".cfg"), true);
            lookup = Config.Bind("General", "Mod to download (Thunderstore URL)", "TheBestAssociatedLargelyLudicrousSillyheadGroup/WellRoundedBalance", "Change this to manually download some other mod? might not work, use Crunderstore URL");
            lookupGUID = Config.Bind("General", "Mod to download (Mod GUID)", "BALLS.WellRoundedBalance", "if blank, will try to convert TS URL").Value;
            if (string.IsNullOrEmpty(lookupGUID)) lookupGUID = lookup.Value.Replace("/", ".");
            modname = lookupGUID == "BALLS.WellRoundedBalance" ? "WRB" : lookupGUID.Split('.').Last();
            installPath = Config.Bind("Versioning", "Last Version (DO NOT CHANGE)", "", "Crunderstore");

            WRBInstalled = Chainloader.PluginInfos.ContainsKey(lookupGUID);

            Download();

            RoR2Application.onLoad += () => input = GameObject.Find("MPEventSystem Player0").GetComponent<MPInput>();
            On.RoR2.UI.MainMenu.BaseMainMenuScreen.OnEnter += (orig, self, mainMenuController) =>
            {
                if (!WRBInstalled)
                {
                    orig(self, mainMenuController);
                    Time.timeScale = 0f;
                    input.eventSystem.cursorOpenerCount++;
                    input.eventSystem.cursorOpenerForGamepadCount++;
                    SimpleDialogBox box = SimpleDialogBox.Create();
                    box.headerToken = new SimpleDialogBox.TokenParamsPair(modname + " has been installed! (hopefully)");
                    box.descriptionToken = new SimpleDialogBox.TokenParamsPair("You may need to restart your game to see " + modname + " working properly. This has to be done only once.");
                    box.AddActionButton(() => DefaultCancel(input.eventSystem), "OK");
                }
            };
        }

        public static void DefaultCancel(MPEventSystem events)
        {
            events.cursorOpenerCount--;
            events.cursorOpenerForGamepadCount--;
            if (SimpleDialogBox.instancesList.Count <= 1) Time.timeScale = 1f;
        }

        public async void Download()
        {
            try
            {
                Log.LogInfo("Fetching Newest " + modname + "Version");
                HttpClient http = new() { BaseAddress = new Uri("https://thunderstore.io") };
                using HttpResponseMessage response = await http.GetAsync("api/experimental/package/" + lookup.Value);
                response.EnsureSuccessStatusCode();
                Log.LogInfo("Successfully Fetched Newest " + modname + " Version");
                string jsonResponse = await response.Content.ReadAsStringAsync();
                Match match = Regex.Match(jsonResponse, @"\""download_url\""\s*:\s*\""https:\/\/thunderstore\.io\/([^\s]+)\/\""");
                if (match.Success && match.Groups.Count > 1)
                {
                    string url = match.Groups[1].Value;
                    if (WRBInstalled && installPath.Value == url)
                    {
                        Log.LogInfo(modname + " seems up to date! skipping download.");
                        return;
                    }
                    else
                    {
                        if (WRBInstalled) Log.LogInfo(modname + " has been updated! Redownloading...");
                        WRBInstalled = false;
                        installPath.Value = url;
                    }
                    Log.LogInfo("Downloading " + url);
                    using HttpResponseMessage raw = await http.GetAsync(url);
                    raw.EnsureSuccessStatusCode();
                    Log.LogInfo("Successfully Fetched Newest " + modname);
                    Stream str = await raw.Content.ReadAsStreamAsync();
                    Log.LogInfo("Creating .zip file, size = " + str.Length);
                    using var zip = new ZipArchive(str, ZipArchiveMode.Read, false);
                    Log.LogInfo("Unpacking...");
                    string currentFolder = Directory.GetParent(Assembly.GetExecutingAssembly().Location).FullName;
                    zip.ExtractToDirectory(currentFolder + "\\temp");
                    foreach (var d in new DirectoryInfo(currentFolder + "\\temp").GetDirectories())
                    {
                        Log.LogInfo("Installing " + d.Name);
                        if (Directory.Exists(currentFolder + "\\" + d.Name)) Directory.Delete(currentFolder + "\\" + d.Name, true);
                        Directory.Move(currentFolder + "\\temp\\" + d.Name, currentFolder + "\\" + d.Name);
                    }
                    foreach (var f in new DirectoryInfo(currentFolder + "\\temp").GetFiles())
                    {
                        Log.LogInfo("Installing " + f.Name);
                        if (File.Exists(currentFolder + "\\" + f.Name)) File.Delete(currentFolder + "\\" + f.Name);
                        File.Move(currentFolder + "\\temp\\" + f.Name, currentFolder + "\\" + f.Name);
                    }
                    Directory.Delete(currentFolder + "\\temp");
                    Log.LogInfo("Successful!");
                    if (Directory.Exists(currentFolder + "\\config"))
                    {
                        Log.LogInfo("Moving configs to proper folder..");
                        foreach (var f in new DirectoryInfo(currentFolder + "\\config").GetFiles())
                        {
                            Log.LogInfo("Overriding " + f.Name);
                            if (File.Exists(Paths.ConfigPath + "\\" + f.Name)) File.Delete(Paths.ConfigPath + "\\" + f.Name);
                            Directory.Move(f.FullName, Paths.ConfigPath + "\\" + f.Name);
                        }
                        foreach (var d in new DirectoryInfo(currentFolder + "\\config").GetDirectories())
                        {
                            Log.LogInfo("Overriding " + d.Name);
                            if (Directory.Exists(Paths.ConfigPath + "\\" + d.Name)) Directory.Delete(Paths.ConfigPath + "\\" + d.Name, true);
                            Directory.Move(currentFolder + "\\config\\" + d.Name, Paths.ConfigPath + "\\" + d.Name);
                        }
                        Directory.Delete(currentFolder + "\\config", true);
                        Log.LogInfo("Also successful!");
                    }
                    Log.LogInfo("Enjoy " + modname + "! -prod");
                }
                else Log.LogError("CANNOT FIND " + modname + "!" + (modname == "WRB" ? " hifuh what did you do :(" : ""));
            } catch (Exception ex) { Log.LogError(ex); }
        }
    }
}
