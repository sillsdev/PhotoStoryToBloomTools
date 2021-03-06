﻿using System.Linq;
using PhotoStoryToBloomConverter.BloomModel.BloomHtmlModel;

namespace PhotoStoryToBloomConverter.BloomModel
{
	//Comparable to the html 'Head' object
	public class BloomMetadata
	{
		public static readonly string[] StandardBloomLinks =
		{
			"basePage.css",
			"languageDisplay.css",
			"previewMode.css",
			"origami.css",
			"Basic Book.css",
			"Traditional-XMatter.css",
			"..\\settingsCollectionStyles.css",
			"..\\customCollectionStyles.css"
		};

		public static readonly string[] DefaultCoverStyles =
		{
			"DIV.coverColor TEXTAREA { background-color: #C2A6BF !important; }\r\nDIV.bloom-page.coverColor { background-color: #C2A6BF !important }",
		};

		public static readonly string[] DefaultUserModifiedStyles =
		{
			".BigWords-style { font-size: 45pt ! important; text-align: center ! important; }\r\n" +
			".Credits-Page-style[lang=\"en\"] { font-size: 8pt ! important; }\r\n" +
			".Credits-Page-style { font-size: 8pt ! important; }",
		};

		public static BloomMetadata DefaultBloomMetadata(string title)
		{
			return new BloomMetadata
			{
				Links = StandardBloomLinks,
				BloomVersion = "2.0",
				Charset = "UTF-8",
				TemplateSource = "Basic Book",
				Generator = "PhotoStoryToBloomConverter 1.0",
				CoverStyles = DefaultCoverStyles,
				UserModifiedStyles = DefaultUserModifiedStyles,
				Title = title,
				LockedDownAsShell = "true"
			};
		}

		public string[] Links { get; set; }
		public string Script { get; set; }
		public string Title { get; set; }
		public string BloomVersion { get; set; }
		public string Charset { get; set; }
		public string TemplateSource { get; set; }
		public string Generator { get; set; }
		public string[] CoverStyles { get; set; }
		public string[] UserModifiedStyles { get; set; }
		public string LockedDownAsShell { get; set; }

		public Head ConvertToHtml()
		{
			return new Head
			{
				Title = new Title { TitleText = Title },
				Script = string.IsNullOrEmpty(Script) ? null : new Script { Src = Script, Type = "text/javascript" },
				Links = Links.Select(linkRef => new Link { Href = linkRef, Rel = "stylesheet", Type = "text/css" }).ToArray(),
				Styles = GetHtmlStyles(),
				Metas = new[]
				{
					new Meta { Charset = Charset },
					new Meta { Name = "Generator", Content = Generator },
					new Meta { Name = "BloomFormatVersion", Content = BloomVersion },
					new Meta { Name = "pageTemplateSource", Content = TemplateSource },
					new Meta { Name = "lockedDownAsShell", Content = LockedDownAsShell }
				}
			};
		}

		private Style[] GetHtmlStyles()
		{
			return CoverStyles.Select(styleCss => new Style {Css = styleCss, Type = "text/css"})
				.Union(UserModifiedStyles.Select(styleCss => new Style {Css = styleCss, Type = "text/css", Title = "userModifiedStyles"}))
				.ToArray();
		}
	}
}
