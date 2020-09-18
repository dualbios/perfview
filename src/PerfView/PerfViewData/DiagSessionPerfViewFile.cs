using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using DiagnosticsHub.Packaging.Interop;
using Microsoft.Diagnostics.Symbols;
using Microsoft.DiagnosticsHub.Packaging.InteropEx;
using Utilities;
using Address = System.UInt64;
using EventSource = EventSources.EventSource;

namespace PerfView.PerfViewData
{
    // Used for new user defined file formats.  

    /// <summary>
    /// Class to represent the Visual Studio .diagsesion file format that is defined
    /// as part of Microsoft.DiagnosticsHub.Packaging
    /// </summary>
    public class DiagSessionPerfViewFile : PerfViewFile
    {
        public override string FormatName { get { return "Diagnostics Session"; } }
        public override string[] FileExtensions { get { return new string[] { ".diagsession" }; } }

        public override IList<PerfViewTreeItem> Children
        {
            get
            {
                if (m_Children == null)
                {
                    m_Children = new List<PerfViewTreeItem>();
                }

                return m_Children;
            }
        }

        public override void Open(Window parentWindow, StatusBar worker, Action doAfter = null)
        {
            if (!m_opened)
            {
                IsExpanded = false;

                worker.StartWork("Opening " + Name, delegate ()
                {
                    OpenImpl(parentWindow, worker);
                    ExecuteOnOpenCommand(worker);

                    worker.EndWork(delegate ()
                    {
                        m_opened = true;

                        FirePropertyChanged("Children");

                        IsExpanded = true;

                        doAfter?.Invoke();
                    });
                });
            }
        }

        public override Action<Action> OpenImpl(Window parentWindow, StatusBar worker)
        {
            worker.Log("Opening diagnostics session file " + Path.GetFileName(FilePath));

            using (DhPackage dhPackage = DhPackage.Open(FilePath))
            {
                // Get all heap dump resources
                AddResourcesAsChildren(worker, dhPackage, HeapDumpPerfViewFile.DiagSessionIdentity, ".gcdump", (localFilePath) =>
                {
                    return HeapDumpPerfViewFile.Get(localFilePath);
                });

                // Get all process dump resources
                AddResourcesAsChildren(worker, dhPackage, ProcessDumpPerfViewFile.DiagSessionIdentity, ".dmp", (localFilePath) =>
                {
                    return ProcessDumpPerfViewFile.Get(localFilePath);
                });

                // Get all ETL files
                AddEtlResourcesAsChildren(worker, dhPackage);

                // Extract the symbols contained in the package
                ExtractSymbolResources(worker, dhPackage);
            }

            return null;
        }

        public override void Close() { }
        public override ImageSource Icon { get { return GuiApp.MainWindow.Resources["FileBitmapImage"] as ImageSource; } }

        /// <summary>
        /// Gets a new local file path for the given resource, extracting it from the .diagsession if required
        /// </summary>
        /// <param name="packageFilePath">The file path to the package</param>
        /// <param name="package">The diagsession package object (opened from the file path)</param>
        /// <param name="resource">The diagsession resource object</param>
        /// <param name="fileExtension">The final extension to use</param>
        /// <param name="alternateName">Alternate name to use for file</param>
        /// <returns>The full local file path to the resource</returns>
        private static string GetLocalFilePath(string packageFilePath, DhPackage package, ResourceInfo resource, string fileExtension, string alternateName = null)
        {
            string localFileName = alternateName ?? Path.GetFileNameWithoutExtension(resource.Name);
            string localFilePath = CacheFiles.FindFile(packageFilePath, "_" + localFileName + fileExtension);

            if (!File.Exists(localFilePath))
            {
                package.ExtractResourceToPath(ref resource.ResourceId, localFilePath);
            }

            return localFilePath;
        }

        /// <summary>
        /// Gets a new local path for the given resource, extracting it from the .diagsession if required
        /// </summary>
        /// <param name="packageFilePath">The file path to the package</param>
        /// <param name="package">The diagsession package object (opened from the file path)</param>
        /// <param name="resource">The diagsession resource object</param>
        /// <returns>The full local path to the resource</returns>
        private static string GetLocalDirPath(string packageFilePath, DhPackage package, ResourceInfo resource)
        {
            string localDirName = resource.Name;
            string localDirPath = CacheFiles.FindFile(packageFilePath, "_" + localDirName);

            if (!Directory.Exists(localDirPath))
            {
                package.ExtractResourceToPath(ref resource.ResourceId, localDirPath);
            }

            return localDirPath;
        }

        /// <summary>
        /// Adds child files from resources in the DhPackage
        /// </summary>
        private void AddResourcesAsChildren(StatusBar worker, DhPackage dhPackage, string resourceIdentity, string fileExtension, Func<string/*localFileName*/, PerfViewFile> getPerfViewFile)
        {
            ResourceInfo[] resources;
            dhPackage.GetResourceInformationByType(resourceIdentity, out resources);

            foreach (var resource in resources)
            {
                string localFilePath = GetLocalFilePath(FilePath, dhPackage, resource, fileExtension);

                worker.Log("Found '" + resource.ResourceId + "' resource '" + resource.Name + "'. Loading ...");

                PerfViewFile perfViewFile = getPerfViewFile(localFilePath);
                perfViewFile.Name = resource.Name;

                Children.Add(perfViewFile);

                worker.Log("Loaded " + resource.Name + ". Loading ...");
            }
        }

        /// <summary>
        /// Extract symbols from the DhPackage
        /// </summary>
        private void ExtractSymbolResources(StatusBar worker, DhPackage dhPackage)
        {
            string symbolCachePath = new SymbolPath(App.SymbolPath).DefaultSymbolCache();

            try
            {
                ResourceInfo[] resources;
                dhPackage.GetResourceInformationByType("DiagnosticsHub.Resource.SymbolCache", out resources);

                foreach (var resource in resources)
                {
                    string localDirPath = GetLocalDirPath(FilePath, dhPackage, resource);

                    worker.Log("Found '" + resource.ResourceId + "' resource '" + resource.Name + "'. Loading ...");

                    foreach (var subPath in Directory.EnumerateDirectories(localDirPath))
                    {
                        // The directories contained in the symbol cache resource _are_ in the symbol cache format
                        // which means directories are in the form of /<file.ext>/<hash>/file.ext so in this case
                        // we use the GetFileName API since it will consider the dictory name a file name.
                        var targetDir = Path.Combine(symbolCachePath, Path.GetFileName(subPath));

                        if (!Directory.Exists(targetDir))
                        {
                            Directory.Move(subPath, targetDir);
                        }
                        else
                        {
                            // The directory exists, so we must merge the two cache directories
                            foreach (var symbolVersionDir in Directory.EnumerateDirectories(subPath))
                            {
                                var targetVersionDir = Path.Combine(targetDir, Path.GetFileName(symbolVersionDir));
                                if (!Directory.Exists(targetVersionDir))
                                {
                                    Directory.Move(symbolVersionDir, targetVersionDir);
                                }
                            }
                        }
                    }

                    // Clean up the extracted symbol cache
                    Directory.Delete(localDirPath, true);
                }
            }
            catch (Exception e)
            {
                worker.Log($"Failed to extract symbols from {Path.GetFileName(FilePath)} ... (Exception: {e.Message})");
            }
        }

        /// <summary>
        /// Adds child files from ETL resources in the DhPackage
        /// </summary>
        private void AddEtlResourcesAsChildren(StatusBar worker, DhPackage dhPackage)
        {
            ResourceInfo[] resources;
            dhPackage.GetResourceInformationByType("DiagnosticsHub.Resource.EtlFile", out resources);

            foreach (var resource in CreateEtlResources(dhPackage, resources))
            {
                worker.Log("Found  resource '" + resource.Name + "'. Loading ...");

                Children.Add(resource);

                worker.Log("Loaded '" + resource.Name + "'. Loading ...");
            }
        }

        /// <summary>
        /// Organize DiagSession ETL resources for the UI
        /// </summary>
        private IEnumerable<PerfViewFile> CreateEtlResources(
            DhPackage dhPackage,
            IEnumerable<ResourceInfo> resources)
        {
            string auxStandardCollectorEtlFile = null;
            var newResources = new List<PerfViewFile>();
            foreach (var resource in resources)
            {
                // If the standard collector auxillary file is present, the standard collection (Diagnostics Hub)
                // created this DiagSession which means we should process the ETL files in bulk.
                if (resource.Name.Equals("sc.user_aux.etl", StringComparison.OrdinalIgnoreCase))
                {
                    auxStandardCollectorEtlFile = GetLocalFilePath(FilePath, dhPackage, resource, ".etl", "sc");
                }
                else
                {
                    var etlFile = GetLocalFilePath(FilePath, dhPackage, resource, ".etl");

                    // The "sc." prefix is considered reserved when loading an ETL from a DiagSession, but we
                    // still want to extract the file, even though the UI will not show it.
                    if (!resource.Name.StartsWith("sc.", StringComparison.OrdinalIgnoreCase))
                    {
                        var file = ETLPerfViewData.Get(etlFile);
                        file.Name = resource.Name;
                        newResources.Add(file);
                    }
                }
            }

            if (auxStandardCollectorEtlFile != null)
            {
                Debug.Assert(File.Exists(auxStandardCollectorEtlFile), "Standard Collector auxillary file must exist to properly handle bulk processing");
                var mergedEtlFilename = Path.GetFileNameWithoutExtension(FilePath);

                var file = ETLPerfViewData.Get(auxStandardCollectorEtlFile);
                file.Name = $"{mergedEtlFilename}.etl (Merged)";

                // Insert the "merged" file at the top of the list
                newResources.Insert(0, file);
            }

            return newResources;
        }
    }
}
