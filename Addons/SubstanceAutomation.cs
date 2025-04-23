using System;
using UnityEditor;
using UnityEngine;
using Adobe.Substance;
using Adobe.Substance.Input;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Addons
{
    public class SubstanceAutomation : IConsoleAddon
    {
        private SubstanceGraphSO substanceGraphSO;
        private List<string> confirmedInputs = new List<string>();
        private bool isPickingFile = false;
        private Dictionary<string, string> commandTooltip;
        private string exportDirectory = null;

        private readonly List<string> addonCommands = new List<string>
        {
            "pick file", "get input >", "export input", "debug inputs", "clear", "help", "exit", "set export dir", "export textures", "update"
        };

        public SubstanceAutomation()
        {
            commandTooltip = new Dictionary<string, string>
            {
                { "pick file", "Opens a file picker to select a SubstanceGraphSO asset from the Unity project." },
                { "get input >", "Confirms an input from the loaded SubstanceGraphSO by its identifier or label. Usage: get input > inputname" },
                { "export input", "Lists all confirmed inputs and marks them as exported." },
                { "debug inputs", "Displays all available inputs in the loaded SubstanceGraphSO." },
                { "clear", "Clears the console history." },
                { "help", "Displays the list of addon commands." },
                { "exit", "Exits the addon environment and returns to the console level." },
                { "set export dir", "Sets the directory where textures will be exported. Usage: set export dir path/to/directory" },
                { "export textures", "Exports all output textures from the loaded SubstanceGraphSO to the specified directory." },
                { "update", "Checks for and installs the latest version of this addon from the online registry." }
            };
        }

        public string GetAddonName()
        {
            return "SubstanceAutomation";
        }

        public List<string> GetCommands()
        {
            return addonCommands;
        }

        public Dictionary<string, string> GetMetadata()
        {
            return new Dictionary<string, string>()
            {
                { "Author", "kim's" },
                { "Website", "https://github.com/yourusername/SubstanceAutomation" },
                { "Version", "0.2" },
                {
                    "Description",
                    "Unity Editor addon for automating Substance Designer texture export and input management"
                }
            };
        }

        public void ShowTooltip(string command, ConsoleEditorWindow console)
        {
            string tooltip;
            if (command.StartsWith("get input >") && command.Length > "get input >".Length)
            {
                command = "get input >";
            }
            if (commandTooltip.TryGetValue(command, out tooltip))
            {
                console.Log($"Tooltip: {tooltip}", Color.cyan);
            }
            else
            {
                console.Log("No tooltip available for this command.", Color.red);
            }
        }

        public async void CheckForUpdates(AddonsDownloader downloader, ConsoleEditorWindow console)
        {
            string addonName = GetAddonName().ToLower();
            console.Log($"Checking for updates for {addonName}...", Color.yellow);

            await Task.Run(async () =>
            {
                try
                {
                    // Fetch the addons list
                    var fetchTask = (Task)downloader.GetType()
                        .GetMethod("FetchAddonsList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                        .Invoke(downloader, new object[] { console });
                    await fetchTask;

                    // Check the version
                    var checkVersionMethod = downloader.GetType()
                        .GetMethod("CheckVersion", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    await (Task)checkVersionMethod.Invoke(downloader, new object[] { addonName, console });

                    // Access centralRegistry via reflection
                    var centralRegistryField = downloader.GetType()
                        .GetField("centralRegistry", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    var centralRegistry = (Dictionary<string, AddonsDownloader.AddonInfo>)centralRegistryField.GetValue(downloader);

                    if (centralRegistry.TryGetValue(addonName, out var addon))
                    {
                        string localVersion = AddonVersion();
                        string onlineVersion = addon.Version;

                        if (IsVersionNewer(onlineVersion, localVersion))
                        {
                            console.Log($"New version {onlineVersion} available for {addonName}. Updating...", Color.yellow);
                            await downloader.UpdateAddon(addonName, console);
                        }
                        else
                        {
                            console.Log($"You are using the latest version of {addonName} ({localVersion}).", Color.green);
                        }
                    }
                }
                catch (System.Exception e)
                {
                    console.Log($"Failed to check for updates: {e.Message}", Color.red);
                }
            });
        }

        private bool IsVersionNewer(string onlineVersion, string localVersion)
        {
            var onlineParts = onlineVersion.Split('.').Select(int.Parse).ToArray();
            var localParts = localVersion.Split('.').Select(int.Parse).ToArray();

            for (int i = 0; i < Math.Min(onlineParts.Length, localParts.Length); i++)
            {
                if (onlineParts[i] > localParts[i])
                    return true;
                if (onlineParts[i] < localParts[i])
                    return false;
            }
            return onlineParts.Length > localParts.Length;
        }

        public bool HandleCommand(string command, ConsoleEditorWindow console)
        {
            command = command.Trim().ToLower();

            if (command == "help")
            {
                console.Log("Available commands: pick file, get input > inputname, export input, debug inputs, clear, set export dir, export textures, update", Color.white);
                return true;
            }
            else if (command == "pick file")
            {
                isPickingFile = true;
                console.Log("Select a SubstanceGraphSO asset in the picker below:", Color.white);
                return true;
            }
            else if (command.StartsWith("get input > "))
            {
                if (substanceGraphSO == null)
                {
                    console.Log("No SubstanceGraphSO loaded. Use 'pick file' first.", Color.red);
                    return true;
                }

                string inputName = command.Substring("get input > ".Length).Trim();
                if (string.IsNullOrEmpty(inputName))
                {
                    console.Log("Input name cannot be empty. Use: get input > inputname", Color.red);
                    return true;
                }

                bool inputFound = false;
                string matchedIdentifier = null;
                foreach (var input in substanceGraphSO.Input)
                {
                    string identifier = input.Description.Identifier;
                    string label = input.Description.Label ?? identifier;
                    if (identifier.ToLower() == inputName.ToLower() || label.ToLower() == inputName.ToLower())
                    {
                        inputFound = true;
                        matchedIdentifier = identifier;
                        break;
                    }
                }

                if (inputFound)
                {
                    confirmedInputs.Add(matchedIdentifier);
                    var input = substanceGraphSO.Input.Find(i => i.Description.Identifier == matchedIdentifier);
                    string type = input.ValueType.ToString();
                    string label = input.Description.Label ?? matchedIdentifier;
                    console.Log(
                        $"Input <color=#0000FF>{matchedIdentifier}</color> confirmed (Label: {label}, Type: {type}).",
                        Color.green);
                }
                else
                {
                    console.Log(
                        $"Input <color=#0000FF>{inputName}</color> not found in SubstanceGraphSO. Try 'debug inputs' to see available inputs.",
                        Color.red);
                }

                return true;
            }
            else if (command == "export input")
            {
                if (substanceGraphSO == null)
                {
                    console.Log("No SubstanceGraphSO loaded. Use 'pick file' first.", Color.red);
                    return true;
                }

                if (confirmedInputs.Count == 0)
                {
                    console.Log("No inputs confirmed.", Color.yellow);
                }
                else
                {
                    console.Log("Confirmed Inputs:", Color.white);
                    foreach (var input in confirmedInputs)
                    {
                        console.Log($"- <color=#0000FF>{input}</color>", Color.white);
                    }

                    console.Log("Inputs exported. You can now proceed with other commands.", Color.green);
                }

                return true;
            }
            else if (command == "debug inputs")
            {
                if (substanceGraphSO == null)
                {
                    console.Log("No SubstanceGraphSO loaded. Use 'pick file' first.", Color.red);
                    return true;
                }

                console.Log("Available Inputs in SubstanceGraphSO:", Color.white);
                foreach (var input in substanceGraphSO.Input)
                {
                    string identifier = input.Description.Identifier ?? "Unnamed";
                    string label = input.Description.Label ?? identifier;
                    string type = input.ValueType.ToString();
                    console.Log($"- Identifier: <color=#0000FF>{identifier}</color>, Label: {label}, Type: {type}",
                        Color.white);
                }

                return true;
            }
            else if (command == "clear")
            {
                console.ClearConsole();
                console.Log("Console cleared.", Color.white);
                return true;
            }
            else if (command == "set export dir")
            {
                string fullPath = EditorUtility.OpenFolderPanel("Select Export Directory", Application.dataPath, "");
                if (string.IsNullOrEmpty(fullPath))
                {
                    console.Log("No directory selected.", Color.red);
                    return true;
                }

                if (!Directory.Exists(fullPath))
                {
                    try
                    {
                        Directory.CreateDirectory(fullPath);
                        console.Log($"Created directory: {fullPath}", Color.green);
                    }
                    catch (System.Exception e)
                    {
                        console.Log($"Failed to create directory: {e.Message}", Color.red);
                        return true;
                    }
                }

                exportDirectory = fullPath;
                console.Log($"Export directory set to: {exportDirectory}", Color.green);
                return true;
            }
            else if (command == "export textures")
            {
                if (substanceGraphSO == null)
                {
                    console.Log("No SubstanceGraphSO loaded. Use 'pick file' first.", Color.red);
                    return true;
                }

                if (string.IsNullOrEmpty(exportDirectory))
                {
                    console.Log("No export directory set. Use 'set export dir' first.", Color.red);
                    return true;
                }

                try
                {
                    foreach (var output in substanceGraphSO.Output)
                    {
                        if (output.OutputTexture != null && output.OutputTexture is Texture2D texture)
                        {
                            string textureName = output.Description.Identifier ?? $"Texture_{output.Index}";
                            string filePath = Path.Combine(exportDirectory, $"{textureName}.png");
                            byte[] textureData = texture.EncodeToPNG();
                            File.WriteAllBytes(filePath, textureData);
                            console.Log($"Exported texture: {filePath}", Color.green);
                        }
                    }

                    AssetDatabase.Refresh();
                    console.Log("All textures exported successfully.", Color.green);
                }
                catch (System.Exception e)
                {
                    console.Log($"Failed to export textures: {e.Message}", Color.red);
                }

                return true;
            }
            else if (command == "update")
            {
                var downloader = console.GetType()
                    .GetField("downloader", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .GetValue(console) as AddonsDownloader;

                if (downloader == null)
                {
                    console.Log("AddonsDownloader not found. Ensure it is installed.", Color.red);
                    return true;
                }

                console.Log($"Updating {GetAddonName()}...", Color.yellow);
                downloader.UpdateAddon(GetAddonName().ToLower(), console);
                return true;
            }

            return false;
        }

        public void DrawFilePicker(ConsoleEditorWindow console)
        {
            if (!isPickingFile) return;

            SubstanceGraphSO selectedGraph = EditorGUILayout.ObjectField("Substance Graph", substanceGraphSO, typeof(SubstanceGraphSO), false) as SubstanceGraphSO;

            if (selectedGraph != substanceGraphSO)
            {
                substanceGraphSO = selectedGraph;
                if (substanceGraphSO != null)
                {
                    confirmedInputs.Clear();
                    console.Log($"Loaded SubstanceGraphSO: {substanceGraphSO.Name}", Color.white);
                    console.Log("Engine Loaded, Version 1.0.0", Color.white);
                }
                isPickingFile = false;
            }
        }

        public string AddonVersion()
        {
            return "0.2";
        }
    }
}