using System;
using Avalonia.Media.Fonts;

namespace AluLab.Common
{
	public sealed class FontCollection : EmbeddedFontCollection
	{
		public FontCollection() : base(
			new Uri( "fonts:AluLabFonts", UriKind.Absolute ),
			new Uri( "avares://AluLab.Common/Assets/Fonts", UriKind.Absolute ) )
		{
		}
	}
}
