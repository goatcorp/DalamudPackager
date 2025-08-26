using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Newtonsoft.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DalamudPackager {
    [SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
    [SuppressMessage("ReSharper", "UnusedType.Global")]
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
    public class DalamudPackager : Task {
        private static readonly string[] ImagePaths = {
            "icon.png",
            "image1.png",
            "image2.png",
            "image3.png",
            "image4.png",
            "image5.png",
        };

        /// <summary>
        /// Set this to $(AssemblyName)
        /// </summary>
        [Required]
        public string AssemblyName { get; set; } = null!;

        /// <summary>
        /// Set this to $(ProjectDir)
        /// </summary>
        [Required]
        public string ProjectDir { get; set; } = null!;

        /// <summary>
        /// Set this to $(OutputPath)
        /// </summary>
        [Required]
        public string OutputPath { get; set; } = null!;

        /// <summary>
        /// This can be either "auto", "json", or "yaml"
        /// </summary>
        public string ManifestType { get; set; } = "auto";

        private ManifestKind RealManifestType => this.ManifestType switch {
            "auto" => ManifestKind.Auto,
            "json" => ManifestKind.Json,
            "yaml" => ManifestKind.Yaml,
            _ => throw new ArgumentException("Invalid manifest type: expected either 'auto', 'json', or 'yaml'", nameof(this.ManifestType)),
        };

        public byte VersionComponents { get; set; } = 4;

        public bool MakeZip { get; set; } = false;

        public bool HandleImages { get; set; } = true;

        /// <summary>
        /// Path to images relative to <see cref="OutputPath"/>.
        /// </summary>
        public string ImagesPath { get; set; } = "images";

        public string? Exclude { get; set; }

        public string? Include { get; set; }

        private Lazy<List<string>> ExcludeFiles => new(() => this.StringToList(this.Exclude));

        private Lazy<List<string>> IncludeFiles => new(() => this.StringToList(this.Include));

        private void NormalisePaths() {
            this.ProjectDir = this.NormalisePath(this.ProjectDir);
            this.OutputPath = this.NormalisePath(this.OutputPath);
            this.ImagesPath = this.NormalisePath(this.ImagesPath);
        }

        private string NormalisePath(string path) {
            return path
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar)
                .TrimEnd(Path.DirectorySeparatorChar);
        }

        public override bool Execute() {
            // normalise path attributes
            this.NormalisePaths();

            // load the manifest from the source file
            var manifest = this.LoadManifest();

            if (manifest == null) {
                this.Log.LogError("Could not find manifest file in project directory.");
                return false;
            }

            // verify required fields on the manifest
            if (manifest.LogMissing(this.Log)) {
                return false;
            }

            // set some things automatically from the assembly
            var assemblyName = this.LoadAssemblyInfo();
            manifest.SetProperties(assemblyName, this.VersionComponents);

            // save the json manifest in the output
            this.SaveManifest(manifest);

            // make a zip if specified
            return !this.MakeZip || this.CreateZip();
        }

        private bool CreateZip() {
            // get path of the folder called the assembly name where we'll have the manifest and latest.zip
            var zipOutput = Path.Combine(this.OutputPath, this.AssemblyName);

            // remove the output folder if it exists
            if (Directory.Exists(zipOutput)) {
                Directory.Delete(zipOutput, true);
            }

            // determine file names to zip
            var includeLen = this.IncludeFiles.Value.Count;
            var excludeLen = this.ExcludeFiles.Value.Count;

            if (includeLen > 0 && excludeLen > 0) {
                this.Log.LogError("Specify either Include or Exclude on your DalamudPackager task, not both.");
                return false;
            }

            // File names all using \ as separator
            string[] fileNames;

            if (includeLen == 0 && excludeLen == 0) {
                fileNames = Directory.EnumerateFiles(this.OutputPath, "*", SearchOption.AllDirectories)
                    .Select(file => this.NormalisePath(file.Substring(this.OutputPath.Length + 1)))
                    .ToArray();
            } else if (includeLen > 0) {
                fileNames = this.IncludeFiles.Value.ToArray();
            } else {
                fileNames = Directory.EnumerateFiles(this.OutputPath, "*", SearchOption.AllDirectories)
                    .Select(file => this.NormalisePath(file.Substring(this.OutputPath.Length + 1)))
                    .Where(file => !this.ExcludeFiles.Value.Contains(file))
                    .ToArray();
            }

            // remove any images that will be handled
            if (this.HandleImages) {
                var badPaths = ImagePaths.Select(p => Path.Combine(this.ImagesPath, p)).ToArray();

                fileNames = fileNames
                    .Where(file => !badPaths.Contains(file))
                    .ToArray();
            }

            // create zip of files in the output path
            var zipPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            using (var zipFile = File.Create(zipPath)) {
                using (var zip = new ZipArchive(zipFile, ZipArchiveMode.Create)) {
                    foreach (var file in fileNames) {
                        var filePath = Path.Combine(this.OutputPath, file);
                        zip.CreateEntryFromFile(filePath, file);
                    }
                }
            }

            // create the output folder
            Directory.CreateDirectory(zipOutput);

            // copy manifest to output
            File.Copy(
                Path.Combine(this.OutputPath, $"{this.AssemblyName}.json"),
                Path.Combine(zipOutput, $"{this.AssemblyName}.json")
            );

            // copy images to output
            if (this.HandleImages) {
                var outputImagesPath = Path.Combine(zipOutput, "images");

                foreach (var path in ImagePaths) {
                    var actualPath = Path.Combine(this.OutputPath, this.ImagesPath, path);
                    if (File.Exists(actualPath)) {
                        if (!Directory.Exists(outputImagesPath)) {
                            Directory.CreateDirectory(outputImagesPath);
                        }

                        File.Copy(
                            actualPath,
                            Path.Combine(outputImagesPath, path)
                        );
                    }
                }
            }

            // move zip to output
            File.Move(
                zipPath,
                Path.Combine(zipOutput, "latest.zip")
            );

            return true;
        }

        private AssemblyName LoadAssemblyInfo() {
            var assemblyPath = Path.Combine(this.OutputPath, $"{this.AssemblyName}.dll");
            var fullPath = Path.GetFullPath(assemblyPath);

            return System.Reflection.AssemblyName.GetAssemblyName(fullPath);
        }

        private Manifest? LoadManifest() {
            var exts = this.RealManifestType switch {
                ManifestKind.Auto => new[] { "json", "yaml" },
                ManifestKind.Json => new[] { "json" },
                ManifestKind.Yaml => new[] { "yaml" },
                _ => throw new ArgumentOutOfRangeException(nameof(this.RealManifestType), $"extension doesn't exist for {this.RealManifestType}"),
            };

            foreach (var ext in exts) {
                var manifestPath = Path.Combine(this.ProjectDir, $"{this.AssemblyName}.{ext}");

                if (!File.Exists(manifestPath)) {
                    continue;
                }

                using var manifestFile = File.Open(manifestPath, FileMode.Open);
                using var manifestStream = new StreamReader(manifestFile);

                return ext switch {
                    "json" => LoadJsonManifest(manifestStream),
                    "yaml" => LoadYamlManifest(manifestStream),
                    _ => throw new Exception("unreachable"),
                };
            }

            return null;
        }

        private static Manifest LoadYamlManifest(TextReader reader) {
            var yamlDeserialiser = new DeserializerBuilder()
                .IgnoreUnmatchedProperties()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();

            return yamlDeserialiser.Deserialize<Manifest>(reader);
        }

        private static Manifest LoadJsonManifest(TextReader reader) {
            return JsonSerializer.CreateDefault().Deserialize<Manifest>(new JsonTextReader(reader))!;
        }

        private void SaveManifest(Manifest manifest) {
            var jsonPath = Path.Combine(this.OutputPath, $"{this.AssemblyName}.json");

            var jsonSerialiser = JsonSerializer.Create(new JsonSerializerSettings {
                Formatting = Formatting.Indented,
                NullValueHandling = NullValueHandling.Ignore,
            });

            using var jsonFile = File.Open(jsonPath, FileMode.Create);
            using var jsonStream = new StreamWriter(jsonFile) {
                NewLine = "\n",
            };
            jsonSerialiser.Serialize(jsonStream, manifest);
        }

        private List<string> StringToList(string? s) {
            if (s == null) {
                return new List<string>();
            }

            return s.Split(';')
                .Select(name => this.NormalisePath(name.Trim()))
                .ToList();
        }
    }

    public enum ManifestKind {
        /// <summary>
        /// Automatically searches for JSON manifests, then searches for YAML manifests if no JSON could be found.
        /// </summary>
        Auto,

        /// <summary>
        /// Superior manifest type. Easier for human consumption. Will be converted into JSON for machine consumption
        /// during the build process.
        /// </summary>
        Yaml,

        /// <summary>
        /// Inferior manifest type. Not meant for human consumption.
        /// </summary>
        Json,
    }

    [Serializable]
    public class Manifest {
        /// <summary>
        /// The author/s of the plugin.
        /// </summary>
        public string? Author { get; set; }

        /// <summary>
        /// The public name of the plugin.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// The internal name of the plugin, which should match the assembly name of the plugin.
        /// </summary>
        public string? InternalName { get; set; }

        /// <summary>
        /// The current assembly version of the plugin.
        /// </summary>
        public string? AssemblyVersion { get; set; }

        /// <summary>
        /// Gets the minimum Dalamud assembly version this plugin requires.
        /// </summary>
        public string? MinimumDalamudVersion { get; }

        /// <summary>
        /// A description of the plugins functions.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// The version of the game this plugin works with.
        /// </summary>
        public string ApplicableVersion { get; set; } = "any";

        /// <summary>
        /// An URL to the website or source code of the plugin.
        /// </summary>
        public string? RepoUrl { get; set; }

        /// <summary>
        /// List of tags defined on the plugin.
        /// </summary>
        public List<string>? Tags { get; set; }

        /// <summary>
        /// List of the category tags defined on the plugin.
        /// </summary>
        public List<string>? CategoryTags { get; set; }

        /// <summary>
        /// The API level of this plugin.
        /// </summary>
        public int DalamudApiLevel { get; set; } = 13;

        /// <summary>
        /// Gets the required Dalamud load step for this plugin to load. Takes precedence over LoadPriority.
        /// Valid values are:
        /// 0. During Framework.Tick, when drawing facilities are available.
        /// 1. During Framework.Tick.
        /// 2. No requirement.
        /// </summary>
        public int LoadRequiredState { get; set; }

        /// <summary>
        /// Gets a value indicating whether Dalamud must load this plugin not at the same time with other plugins and the game.
        /// </summary>
        public bool LoadSync { get; set; }
        
        /// <summary>
        /// Gets a value indicating whether Dalamud can unload the plugin outside of the Framework thread.
        /// </summary>
        public bool CanUnloadAsync { get; set; }

        /// <summary>
        /// Load priority for this plugin. Higher values means higher priority. 0 is default priority.
        /// </summary>
        public int LoadPriority { get; set; }

        /// <summary>
        /// Array of links to screenshots/other images that will be displayed. These images must be 730x380 resolution, with a maximum of 5 images.
        /// </summary>
        public List<string>? ImageUrls { get; set; }

        /// <summary>
        /// Link to a 512x512 icon for your plugin.
        /// </summary>
        public string? IconUrl { get; set; }

        /// <summary>
        /// One-sentence description of your plugin.
        /// </summary>
        public string? Punchline { get; set; }

        /// <summary>
        /// Small description of recent changes to your plugin, only shown for people who have the plugin installed.
        /// </summary>
        public string? Changelog { get; set; }

        /// <summary>
        /// Gets a value indicating whether this plugin accepts feedback.
        /// </summary>
        public bool AcceptsFeedback { get; set; } = true;

        /// <summary>
        /// Gets a message that is shown to users when sending feedback.
        /// </summary>
        public string? FeedbackMessage { get; set; }

        internal bool LogMissing(TaskLoggingHelper log) {
            var anyNull = this.Name == null || this.Author == null || this.Description == null || this.Punchline == null;

            if (this.Name == null) {
                log.LogError("Plugin name is required in your manifest.");
            }

            if (this.Author == null) {
                log.LogError("Author name is required in your plugin manifest.");
            }

            if (this.Description == null) {
                log.LogError("Description is required in your plugin manifest.");
            }

            if (this.Punchline == null) {
                log.LogError("Punchline is required in your plugin manifest.");
            }

            return anyNull;
        }

        internal void SetProperties(AssemblyName assembly, byte components) {
            this.AssemblyVersion = assembly.Version!.ToString(components);
            this.InternalName = assembly.Name;
        }
    }
}
