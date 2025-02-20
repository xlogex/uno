﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Uno.Extensions;
using Windows.UI.Xaml;
using CoreGraphics;
using Foundation;
using UIKit;
using Uno.UI.Extensions;
using Windows.UI.Xaml.Media;
using Windows.UI.Text;
using Uno.Foundation.Logging;
using Uno.UI;

#if NET6_0_OR_GREATER
using ObjCRuntime;
#endif

namespace Windows.UI
{
	internal static class UIFontHelper
	{
		private static Func<nfloat, FontWeight, FontStyle, FontFamily, nfloat?, UIFont> _tryGetFont;

		private const int DefaultUIFontPreferredBodyFontSize = 17;
		private static float? DefaultPreferredBodyFontSize = UIFont.PreferredBody.FontDescriptor.FontAttributes.Size;

		static UIFontHelper()
		{
			_tryGetFont = InternalTryGetFont;
			_tryGetFont = _tryGetFont.AsMemoized();
		}

		internal static UIFont TryGetFont(nfloat size, FontWeight fontWeight, FontStyle fontStyle, FontFamily requestedFamily, float? preferredBodyFontSize = null)
		{
			return _tryGetFont(size, fontWeight, fontStyle, requestedFamily, preferredBodyFontSize ?? DefaultPreferredBodyFontSize);
		}

		/// <summary>
		/// Based on iOS settings of text size in General->Accessibility->LargerText
		/// </summary>
		/// <param name="size"></param>
		/// <returns>Scaled font size</returns>
		internal static nfloat GetScaledFontSize(nfloat size, float? preferredBodyFontSize = null)
		{
			return GetScaledFontSize(size, preferredBodyFontSize ?? DefaultPreferredBodyFontSize);
		}

		/// <summary>
		/// Based on iOS settings of text size in General->Accessibility->LargerText.
		/// This overload was created to better work with memoized functions
		/// </summary>
		/// <param name="size"></param>
		/// <param name="basePreferredSize"></param>
		/// <returns></returns>
		private static nfloat GetScaledFontSize(nfloat size, nfloat? basePreferredSize)
		{
			//We need to scale the font size depending on the PreferredBody size. This is modified by accessibility settings.
			//https://developer.xamarin.com/api/member/MonoTouch.UIKit.UIFont.GetPreferredFontForTextStyle/
			if (FeatureConfiguration.Font.IgnoreTextScaleFactor)
			{
				return size;
			}

			return size * (basePreferredSize / DefaultUIFontPreferredBodyFontSize) ?? (float)1.0;
		}

		private static UIFont InternalTryGetFont(nfloat size, FontWeight fontWeight, FontStyle fontStyle, FontFamily requestedFamily, nfloat? basePreferredSize)
		{
			UIFont font = null;

			size = GetScaledFontSize(size, basePreferredSize);

			if (requestedFamily?.Source != null)
			{
				var fontFamilyName = FontFamilyHelper.RemoveUri(requestedFamily.Source);

				// If there's a ".", we assume there's an extension and that it's a font file path.
				font = fontFamilyName.Contains(".") ? GetCustomFont(size, fontFamilyName, fontWeight, fontStyle) : GetSystemFont(size, fontWeight, fontStyle, fontFamilyName);
			}

			return font ?? GetDefaultFont(size, fontWeight, fontStyle);
		}

		private static UIFont GetDefaultFont(nfloat size, FontWeight fontWeight, FontStyle fontStyle)
		{
			if (!UIDevice.CurrentDevice.CheckSystemVersion(8, 2))
			{
				return GetSystemFont(size, fontWeight, fontStyle, "HelveticaNeue");
			}

			return ApplyStyle(UIFont.SystemFontOfSize(size, fontWeight.ToUIFontWeight()), size, fontStyle);
		}

		#region Load Custom Font
		private static UIFont GetCustomFont(nfloat size, string fontPath, FontWeight fontWeight, FontStyle fontStyle)
		{
			UIFont font;
			//In Windows we define FontFamily with the path to the font file followed by the font family name, separated by a #
			if (fontPath.Contains("#"))
			{
				var pathParts = fontPath.Split(new[] { '#' });
				var file = pathParts[0];
				var familyName = pathParts[1];

				font = GetFontFromFamilyName(size, familyName) ?? GetFontFromFile(size, file);
			}
			else
			{
				font = GetFontFromFile(size, fontPath);
			}

			if (font == null)
			{
				font = GetDefaultFont(size, fontWeight, fontStyle);
			}

			font = ApplyWeightAndStyle(font, size, fontWeight, fontStyle);

			return font;
		}

		private static UIFont ApplyWeightAndStyle(UIFont font, nfloat size, FontWeight fontWeight, FontStyle fontStyle)
		{
			font = ApplyWeight(font, size, fontWeight);
			font = ApplyStyle(font, size, fontStyle);
			return font;
		}

		private static UIFont ApplyWeight(UIFont font, nfloat size, FontWeight fontWeight)
		{
			if (fontWeight.Weight == FontWeights.Bold.Weight && !font.FontDescriptor.SymbolicTraits.HasFlag(UIFontDescriptorSymbolicTraits.Bold))
			{
				var descriptor = font.FontDescriptor.CreateWithTraits(font.FontDescriptor.SymbolicTraits | UIFontDescriptorSymbolicTraits.Bold);
				if (descriptor != null)
				{
					font = UIFont.FromDescriptor(descriptor, size);
				}
				else
				{
					typeof(UIFontHelper).Log().Error($"Can't apply Bold on font \"{font.Name}\". Make sure the font supports it or use another FontFamily.");
				}
			}
			else if (
				fontWeight.Weight != FontWeights.SemiBold.Weight && // For some reason, when we load a Semibold font, we must keep the native Bold flag.
				fontWeight.Weight < FontWeights.Bold.Weight &&
				font.FontDescriptor.SymbolicTraits.HasFlag(UIFontDescriptorSymbolicTraits.Bold))
			{
				var descriptor = font.FontDescriptor.CreateWithTraits(font.FontDescriptor.SymbolicTraits & ~UIFontDescriptorSymbolicTraits.Bold);
				if (descriptor != null)
				{
					font = UIFont.FromDescriptor(descriptor, size);
				}
				else
				{
					typeof(UIFontHelper).Log().Error($"Can't remove Bold from font \"{font.Name}\". Make sure the font supports it or use another FontFamily.");
				}
			}

			return font;
		}

		private static UIFont ApplyStyle(UIFont font, nfloat size, FontStyle fontStyle)
		{
			if (fontStyle == FontStyle.Italic && !font.FontDescriptor.SymbolicTraits.HasFlag(UIFontDescriptorSymbolicTraits.Italic))
			{
				var descriptor = font.FontDescriptor.CreateWithTraits(font.FontDescriptor.SymbolicTraits | UIFontDescriptorSymbolicTraits.Italic);
				if (descriptor != null)
				{
					font = UIFont.FromDescriptor(descriptor, size);
				}
				else
				{
					typeof(UIFontHelper).Log().Error($"Can't apply Italic on font \"{font.Name}\". Make sure the font supports it or use another FontFamily.");
				}
			}
			else if (fontStyle == FontStyle.Normal && font.FontDescriptor.SymbolicTraits.HasFlag(UIFontDescriptorSymbolicTraits.Italic))
			{
				var descriptor = font.FontDescriptor.CreateWithTraits(font.FontDescriptor.SymbolicTraits & ~UIFontDescriptorSymbolicTraits.Italic);
				if (descriptor != null)
				{
					font = UIFont.FromDescriptor(descriptor, size);
				}
				else
				{
					typeof(UIFontHelper).Log().Error($"Can't remove Italic from font \"{font.Name}\". Make sure the font supports it or use another FontFamily.");
				}
			}

			return font;
		}

		private static UIFont GetFontFromFamilyName(nfloat size, string familyName)
		{
			//If only one font exists for this family name, use it. Otherwise we will need to inspect the file for the right font name
			var fontNames = UIFont.FontNamesForFamilyName(familyName);
			return fontNames.Count() == 1 ? UIFont.FromName(fontNames[0], size) : null;
		}

		private static UIFont GetFontFromFile(nfloat size, string file)
		{
			var fileName = Path.GetFileNameWithoutExtension(file);
			var fileExtension = Path.GetExtension(file)?.Replace(".", "");

			var url = NSBundle
				.MainBundle
				.GetUrlForResource(
					name: fileName,
					fileExtension: fileExtension,
					subdirectory: "Fonts"
				);

			if (url == null)
			{
				return null;
			}

			var fontData = NSData.FromUrl(url);
			if (fontData == null)
			{
				return null;
			}

			//iOS loads UIFonts based on the PostScriptName of the font file
			using (var fontProvider = new CGDataProvider(fontData))
			{
				using (var font = CGFont.CreateFromProvider(fontProvider))
				{
					return font != null ? UIFont.FromName(font.PostScriptName, size) : null;
				}
			}
		}
		#endregion

		#region Load System Font
		private static UIFont GetSystemFont(nfloat size, FontWeight fontWeight, FontStyle fontStyle, string fontFamilyName)
		{
			//based on Fonts available @ http://iosfonts.com/
			//for Windows parity feature, we will not support FontFamily="HelveticaNeue-Bold" (will ignore Bold and must be set by FontWeight property instead)
			var rootFontFamilyName = fontFamilyName.Split(new[] { '-' }).FirstOrDefault();

			if (rootFontFamilyName.HasValue())
			{
				var font = new StringBuilder(rootFontFamilyName);
				if (fontWeight != FontWeights.Normal || fontStyle == FontStyle.Italic)
				{
					font.Append("-");
					font.Append(GetFontWeight(fontWeight));
					font.Append(GetFontStyle(fontStyle));
				}

				var updatedFont = UIFont.FromName(font.ToString(), size);
				if (updatedFont != null)
				{
					return updatedFont;
				}

				font.Log().Warn("Failed to apply Font " + font);

				return UIFont.FromName(rootFontFamilyName, size);
			}

			return null;
		}

		private static string GetFontWeight(FontWeight fontWeight)
		{
			if (fontWeight == FontWeights.Normal)
			{
				return string.Empty;
			}
			if (fontWeight == FontWeights.Black)
			{
				return "Black";
			}
			if (fontWeight == FontWeights.Bold)
			{
				return "Bold";
			}
			if (fontWeight == FontWeights.DemiBold)
			{
				return "DemiBold";
			}
			if (fontWeight == FontWeights.ExtraBlack)
			{
				return "ExtraBlack";
			}
			if (fontWeight == FontWeights.Heavy ||
				//non corresponding FontWeight in iOS, fallback to FontWeight that makes sense
				fontWeight == FontWeights.ExtraBold ||
				fontWeight == FontWeights.UltraBlack ||
				fontWeight == FontWeights.UltraBlack ||
				fontWeight == FontWeights.UltraBold)
			{
				return "Heavy";
			}
			if (fontWeight == FontWeights.Light)
			{
				return "Light";
			}
			if (fontWeight == FontWeights.Medium)
			{
				return "Medium";
			}
			if (fontWeight == FontWeights.Regular)
			{
				return "Regular";
			}
			if (fontWeight == FontWeights.SemiBold)
			{
				return "SemiBold";
			}
			if (fontWeight == FontWeights.Thin
				|| fontWeight == FontWeights.SemiLight)
			{
				return "Thin";
			}
			if (fontWeight == FontWeights.UltraLight || fontWeight == FontWeights.ExtraLight)
			{
				return "UltraLight";
			}
			return string.Empty;
		}

		private static string GetFontStyle(FontStyle fontStyle)
		{
			if (fontStyle == FontStyle.Italic)
			{
				return "Italic";
			}
			return string.Empty;
		}
		#endregion
	}
}
