using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using PhotoStoryToBloomConverter.BloomModel;
using PhotoStoryToBloomConverter.PS3Model;
using PhotoStoryToBloomConverter.Utilities;
using SIL.Extensions;

namespace PhotoStoryToBloomConverter
{
	class Project
	{
		private readonly string _projectXmlPath;
		private readonly AudioHelper _audioHelper = new AudioHelper();

		public Project(string projectXmlPath)
		{
			_projectXmlPath = projectXmlPath;
		}

		public bool Convert(string destinationFolder, string projectName, string projectCode, IEnumerable<string> docxPaths,
			string bloomPath, bool overwrite, bool includeReferences, PhotoStoryProject photoStoryProject = null)
		{
			if (docxPaths == null)
				docxPaths = new List<string>();

			if (photoStoryProject == null)
			{
				photoStoryProject = Ps3AndBloomSerializer.DeserializePhotoStoryXml(_projectXmlPath);
				if (String.IsNullOrEmpty(projectName))
					projectName = photoStoryProject.GetProjectName();
			}

			var textByLanguage = new Dictionary<Language, IList<SourceText>>();
			foreach (var docxPath in docxPaths)
			{
				var language = Path.GetFileNameWithoutExtension(docxPath).GetLanguageFromFileNameWithoutExtension();
				if (language != Language.Unknown && textByLanguage.ContainsKey(language))
					continue;
				if (TextExtractor.TryExtractText(docxPath, out var languageText))
					textByLanguage.Add(language, languageText);
				else
					Console.WriteLine($"Error: Could not process {language.ToString()} Word document for {projectName}.");
			}

			if (!textByLanguage.ContainsKey(Language.English))
			{
				Console.WriteLine($"Error: Could not find document with corresponding English text for {projectName}.");
				return false;
			}

			var allPagesInAllLanguages = new List<List<KeyValuePair<Language, SourceText>>>();

			// Perhaps a bad assumption, but for now we will assume that if a non-English language
			// has the same number of elements as the English, that it is up-to-date. If not, don't include it.
			var englishTextElementCount = textByLanguage[Language.English].Count;
			var languagesToExclude = new List<Language>();
			foreach (var language in textByLanguage)
			{
				if (language.Value.Count != englishTextElementCount)
				{
					Console.WriteLine($"Excluding {language.Key} because it is out of sync with English");
					languagesToExclude.Add(language.Key);
				}
			}
			textByLanguage.RemoveAll(l => languagesToExclude.Contains(l.Key));

			string alternateTitles = null;
			for (int index = 0; index < textByLanguage[Language.English].Count; index++)
			{
				var allTranslationsOfThisPage = new List<KeyValuePair<Language, SourceText>>(6);
				foreach (var language in textByLanguage)
				{
					var sourceText = language.Value[index];
					if (sourceText.TextType == TextType.Title && !string.IsNullOrWhiteSpace(projectCode))
					{
						if (!sourceText.Text.StartsWith(projectCode))
							sourceText.Text = $"{projectCode} {sourceText.Text}";
						if (language.Key == Language.English)
						{
							// We decided to give preference to the title in the English docx rather than the original PhotoStory project
							projectName = sourceText.Text;
						}
					}

					if (sourceText.TextType == TextType.AlternateTitles)
					{
						if (language.Key == Language.English)
							alternateTitles = sourceText.Text;
						allTranslationsOfThisPage = null;
						continue;
					}

					if (includeReferences && sourceText.TextType == TextType.Text)
						sourceText.Text = sourceText.Reference + "\n" + sourceText.Text;
					allTranslationsOfThisPage.Add(new KeyValuePair<Language, SourceText>(language.Key, sourceText));
				}

				if (allTranslationsOfThisPage != null)
					allPagesInAllLanguages.Add(allTranslationsOfThisPage);
			}

			var convertedProjectDirectory = Path.Combine(destinationFolder, IOHelper.SanitizeFileOrDirectoryName(projectName));
			if (Directory.Exists(convertedProjectDirectory) && !overwrite)
			{
				Console.WriteLine($"Error: A book already exists with the name {projectName}.");
				return false;
			}
			if (Directory.Exists(convertedProjectDirectory) && overwrite)
				Program.DeleteAllFilesAndFoldersInDirectory(convertedProjectDirectory);
			else
				Directory.CreateDirectory(convertedProjectDirectory);

			//Three things needed for a bloom book:
			//  book assets (images, narration audio, background audio)
			//  bloom book css and images
			//  the actual book, a generated html file built from the photostory project
			CopyAssetsAndResources(Path.GetDirectoryName(_projectXmlPath), convertedProjectDirectory);
			ConvertToBloom(photoStoryProject, Path.Combine(convertedProjectDirectory, $"{projectName}.htm"), projectName, allPagesInAllLanguages, alternateTitles, includeReferences);

			var hydrationArguments =
				$"hydrate --preset shellbook --bookpath \"{convertedProjectDirectory}\" --vernacularisocode en";
			bool hydrateSuccessful;
			try
			{
				using (var process = Process.Start(bloomPath, hydrationArguments))
				{
					process.WaitForExit();
					hydrateSuccessful = process.ExitCode == 0;
				}
			}
			catch
			{
				hydrateSuccessful = false;
			}
			if (!hydrateSuccessful)
				Console.WriteLine("ERROR: Unable to hydrate {0}", projectName);
			else
			{
				Console.WriteLine("Successfully converted {0}", projectName);
				Console.WriteLine("   Languages: {0}", string.Join(", ", textByLanguage.Keys));
				Console.WriteLine();
			}

			return true;
		}

		//The assumption is that the wp3 archive only contains assets and a project.xml file. We convert the .xml file and copy the images and audio tracks.
		private void CopyAssetsAndResources(string sourceFolderPath, string destinationFolderPath)
		{
			var audioFiles = new List<Tuple<string, bool>>();
			foreach (var filePath in Directory.EnumerateFiles(sourceFolderPath))
			{
				var filename = Path.GetFileName(filePath);
				if (filename.Equals("project.xml"))
					continue;

				if (AudioHelper.AudioFileNeedsConversion(filename))
					audioFiles.Add(new Tuple<string, bool>(filename, true));
				else if (AudioHelper.IsAudioFile(filename))
					audioFiles.Add(new Tuple<string, bool>(filename, false));
				else
					// should be image assets
					File.Copy(Path.Combine(sourceFolderPath, filename), Path.Combine(destinationFolderPath, filename));
			}
			if (audioFiles.Count > 0)
				_audioHelper.ProcessAudioFiles(sourceFolderPath, destinationFolderPath, audioFiles);
		}

		//Pulls in all the gathered information for the project and creates a single bloom book html file at destinationFile
		private void ConvertToBloom(PhotoStoryProject project, string destinationFile, string bookName,
			IList<List<KeyValuePair<Language, SourceText>>> allPagesInAllLanguages, string alternateTitles, bool includeReferences)
		{
			var destinationDirectory = Path.GetDirectoryName(destinationFile);
			var document = new BloomDocument(project, bookName, destinationDirectory, allPagesInAllLanguages, _audioHelper.Duplicates, alternateTitles, includeReferences);
			Ps3AndBloomSerializer.SerializeBloomHtml(document.ConvertToHtml(), destinationFile);
			AddMetaJson(destinationDirectory);
		}

		private void AddMetaJson(string destinationDirectory)
		{
			// TODO: If we include narration audio in any templates we create, we should include the 'media:audio' tag as well.
			// Adding these tags was our plan at one point, but we are just leaving them off for now.
			//File.WriteAllText(Path.Combine(destinationDirectory, "meta.json"), "{tags:['media:fulltext', 'media:kbanimation', 'media:music', 'tag:BibleStoryMultimedia']}");
			File.Create(Path.Combine(destinationDirectory, "meta.json")).Close();
		}
	}
}
