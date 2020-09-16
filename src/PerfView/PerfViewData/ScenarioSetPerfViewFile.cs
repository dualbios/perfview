using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using Diagnostics.Tracing.StackSources;
using Microsoft.Diagnostics.Tracing.Stacks;

namespace PerfView.PerfViewData
{
    public class ScenarioSetPerfViewFile : PerfViewFile
    {
        public override string FormatName { get { return "Scenario Set"; } }
        public override string[] FileExtensions { get { return new string[] { ".scenarioSet.xml" }; } }

        public override string HelpAnchor { get { return "ViewingMultipleScenarios"; } }

        protected internal override StackSource OpenStackSourceImpl(TextWriter log)
        {
            Dictionary<string, string> pathDict = null;
            XmlReaderSettings settings = new XmlReaderSettings() { IgnoreWhitespace = true, IgnoreComments = true };
            using (Stream dataStream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete))
            {
                using (XmlReader reader = XmlTextReader.Create(dataStream, settings))
                {
                    pathDict = DeserializeConfig(reader, log);
                }
            }

            if (pathDict.Count == 0)
            {
                throw new ApplicationException("No scenarios found");
            }

            // Open XmlStackSources on each of our paths.
            var sources = pathDict.Select(
                (pair, idx) =>
                {
                    string name = pair.Key, path = pair.Value;
                    log.WriteLine("[Opening [{0}/{1}] {2} ({3})]", idx + 1, pathDict.Count, name, path);
                    var source = new XmlStackSource(path);
                    return new KeyValuePair<string, StackSource>(name, source);
                }
            ).ToList(); // Copy to list to prevent repeated enumeration from having an effect.

            return new AggregateStackSource(sources);
        }

        protected internal override void ConfigureStackWindow(string stackSourceName, StackWindow stackWindow)
        {
            ConfigureAsEtwStackWindow(stackWindow, false, false);
        }

        #region private

        /// <summary>
        /// Search for scenario data files matching a pattern, and add them to a dictionary.
        /// </summary>
        /// <param name="filePattern">The wildcard file pattern to match. Must not be null.</param>
        /// <param name="namePattern">The pattern by which to name scenarios. If null, defaults to "scenario $1".</param>
        /// <param name="includePattern">If non-null, a pattern which must be matched for the scenario to be added</param>
        /// <param name="excludePattern">If non-null, a pattern which if matched causes the scenario to be excluded</param>
        /// <param name="dict">The dictionary to which to add the scenarios found.</param>
        /// <param name="log">A log file to write log messages.</param>
        /// <param name="baseDir">
        /// The directory used to resolve relative paths.
        /// Defaults to the directory of the XML file represented by this ScenarioSetPerfViewFile.
        /// </param>
        private void AddScenariosToDictionary(
            string filePattern, string namePattern, string includePattern, string excludePattern,
            Dictionary<string, string> dict, TextWriter log,
            string baseDir = null)
        {
            Debug.Assert(filePattern != null);

            if (baseDir == null)
            {
                baseDir = Path.GetDirectoryName(FilePath);
            }

            if (namePattern == null)
            {
                namePattern = "scenario $1";
            }

            string replacePattern = Regex.Escape(filePattern)
                .Replace(@"\*", @"([^\\]*)")
                .Replace(@"\?", @"[^\\]");

            if (!(filePattern.EndsWith(".perfView.xml", StringComparison.OrdinalIgnoreCase) ||
                  filePattern.EndsWith(".perfView.xml.zip", StringComparison.OrdinalIgnoreCase)))
            {
                throw new ApplicationException("Files must be PerfView XML files");
            }

            string pattern = Path.GetFileName(filePattern);
            string dir = Path.GetDirectoryName(filePattern);

            // Tack on the base directory if we're not already an absolute path.
            if (!Path.IsPathRooted(dir))
            {
                dir = Path.Combine(baseDir, dir);
            }

            var replaceRegex = new Regex(replacePattern, RegexOptions.IgnoreCase);
            var defaultRegex = new Regex(@"(.*)", RegexOptions.IgnoreCase);

            // TODO: Directory.GetFile
            foreach (string file in Directory.GetFiles(dir, pattern, SearchOption.AllDirectories))
            {
                // Filter out those that don't match the include pattern 
                if (includePattern != null && !Regex.IsMatch(file, includePattern))
                {
                    continue;
                }
                // or do match the exclude pattern.  
                if (excludePattern != null && Regex.IsMatch(file, excludePattern))
                {
                    continue;
                }

                string name = null;
                if (namePattern != null)
                {
                    var match = replaceRegex.Match(file);

                    // We won't have a group to match if there were no wildcards in the pattern.
                    if (match.Groups.Count < 1)
                    {
                        match = defaultRegex.Match(GetFileNameWithoutExtension(file));
                    }

                    name = match.Result(namePattern);
                }

                dict[name] = file;

                log.WriteLine("Added '{0}' ({1})", name, file);
            }
        }

        /// <summary>
        /// Deserialize a scenario XML config file.
        /// </summary>
        /// <param name="reader">The XmlReader containing the data to deserialize.</param>
        /// <param name="log">The TextWriter to log output to.</param>
        /// <returns>A Dictionary mapping scenario names to .perfView.xml(.zip) data file paths.</returns>
        /// <remarks>
        /// Scenario XML config files contain a ScenarioSet root element. That element contains
        /// one or more Scenarios elements. A Scenarios element has two attributes: "files" is a required
        /// filename pattern, and namePattern is a pattern by which to name the scenario.
        /// 
        /// files is a required attribute specifying where to find the data files for the scenario(s). Wildcards
        /// are acceptable - any files matched by the wildcard will be added to the scenario set. All paths are
        /// relative to the location of the XML config file.
        /// 
        /// namePattern can contain substitutions as specified in Regex.Replace. Each * in the wildcard
        /// pattern will be converted to an appropriate capturing group. If no wildcards are specified, $1 will be
        /// set to the base name of the data file as specified by <see cref="PerfViewFile.GetFileNameWithoutExtension"/>.
        /// 
        /// files is a required attribute. namePattern is optional, and defaults to "scenario $1".
        /// 
        /// If multiple scenarios have the same name, scenarios later in the file will override scenarios
        /// earlier in the file.
        /// </remarks>
        /// <example>
        /// Example config file:
        /// <ScenarioSet>
        /// <Scenarios files="*.perfView.xml.zip" namePattern="Example scenario [$1]" />
        /// <Scenarios files="foo.perfView.xml.zip" namePattern="Example scenario [baz]" />
        /// </ScenarioSet>
        /// 
        /// Files in the directory:
        /// foo.perfView.xml.zip
        /// bar.perfView.xml.zip
        /// baz.perfView.xml.zip
        /// 
        /// Return value:
        /// "Example scenario [foo]" => "foo.perfView.xml.zip"
        /// "Example scenario [bar]" => "bar.perfView.xml.zip"
        /// "Example scenario [baz]" => "foo.perfView.xml.zip"
        /// </example>
        private Dictionary<string, string> DeserializeConfig(XmlReader reader, TextWriter log)
        {
            var pathDict = new Dictionary<string, string>();

            if (!reader.ReadToDescendant("ScenarioSet"))
            {
                throw new ApplicationException("The file " + FilePath + " does not have a Scenario element");
            }

            if (!reader.ReadToDescendant("Scenarios"))
            {
                throw new ApplicationException("No scenarios specified");
            }

            do
            {
                string filePattern = reader["files"];
                string namePattern = reader["namePattern"];
                string includePattern = reader["includePattern"];
                string excludePattern = reader["excludePattern"];

                if (filePattern == null)
                {
                    throw new ApplicationException("File path is required.");
                }

                AddScenariosToDictionary(filePattern, namePattern, includePattern, excludePattern, pathDict, log);
            }
            while (reader.ReadToNextSibling("Scenarios"));

            return pathDict;
        }

        #endregion
    }
}