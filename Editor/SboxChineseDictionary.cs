using System;
using System.Collections.Generic;
using System.Linq;

namespace Editor;

static class SboxChineseDictionary
{
	// Missing text collection is language-specific so we can iterate on one locale without polluting another.
	static readonly object MissingLock = new();
	static readonly Dictionary<string, SortedSet<string>> MissingByLanguage = new( StringComparer.OrdinalIgnoreCase );

	static readonly Dictionary<string, string> Exact = new( StringComparer.OrdinalIgnoreCase )
	{
	};

	public static string Translate( string value )
	{
		if ( string.IsNullOrEmpty( value ) )
			return value;

		// API-derived strings get first dibs because the same short label can appear in multiple contexts.
		if ( SboxTranslationFiles.TryTranslateApi( value, out var translated ) )
			return translated;

		if ( TryTranslateDirect( value, out translated ) )
			return translated;

		if ( TryTranslateNormalizedRichText( value, out translated ) )
			return translated;

		if ( TryTranslateShortcutSuffix( value, out translated ) )
			return translated;

		if ( value.Contains( '/' ) )
			return string.Join( "/", value.Split( '/' ).Select( TranslatePathPart ) );

		if ( value.Contains( "s&box editor", StringComparison.OrdinalIgnoreCase ) &&
			TryTranslateDirect( "s&box editor", out var editorName ) )
		{
			return value.Replace( "s&box editor", editorName, StringComparison.OrdinalIgnoreCase );
		}

		CollectMissingText( value );
		return value;
	}

	public static string TranslateControlSheetToolTip( string value )
	{
		if ( string.IsNullOrEmpty( value ) )
			return value;

		value = Translate( value );
		value = ReplaceTranslatedSegment( value, "Property Name:" );
		value = ReplaceTranslatedSegment( value, "Default Value:" );
		value = ReplaceTranslatedHtmlType( value, "bool" );
		value = ReplaceTranslatedHtmlType( value, "byte" );
		value = ReplaceTranslatedHtmlType( value, "decimal" );
		value = ReplaceTranslatedHtmlType( value, "double" );
		value = ReplaceTranslatedHtmlType( value, "float" );
		value = ReplaceTranslatedHtmlType( value, "int" );
		value = ReplaceTranslatedHtmlType( value, "long" );
		value = ReplaceTranslatedHtmlType( value, "object" );
		value = ReplaceTranslatedHtmlType( value, "short" );
		value = ReplaceTranslatedHtmlType( value, "string" );
		value = ReplaceTranslatedHtmlType( value, "uint" );
		value = ReplaceTranslatedHtmlType( value, "ulong" );
		value = ReplaceTranslatedHtmlType( value, "ushort" );

		return value;
	}

	static bool TryTranslateShortcutSuffix( string value, out string translated )
	{
		translated = null;

		var start = value.LastIndexOf( " [", StringComparison.Ordinal );
		if ( start < 0 || !value.EndsWith( "]", StringComparison.Ordinal ) )
			return false;

		var baseText = value[..start];
		var suffix = value[start..];
		if ( !TryTranslateDirect( baseText, out var baseTranslated ) )
			return false;

		translated = $"{baseTranslated}{suffix}";
		return true;
	}

	static bool TryTranslateNormalizedRichText( string value, out string translated )
	{
		translated = null;

		if ( value.IndexOf( '\n' ) < 0 && value.IndexOf( '\r' ) < 0 )
			return false;

		// XML summaries often arrive with indentation from generated docs; normalize before looking them up.
		var normalized = NormalizeRichTextLookup( value );
		if ( string.Equals( normalized, value, StringComparison.Ordinal ) )
			return false;

		return TryTranslateDirect( normalized, out translated );
	}

	public static string[] GetMissingTexts()
	{
		lock ( MissingLock )
		{
			return GetMissingSetForCurrentLanguage().ToArray();
		}
	}

	public static void ClearMissingTexts()
	{
		lock ( MissingLock )
		{
			GetMissingSetForCurrentLanguage().Clear();
		}
	}

	static string TranslatePathPart( string value )
	{
		var prefix = value.StartsWith( "#" ) ? "#" : "";
		var body = prefix.Length == 0 ? value : value[1..];
		var suffixIndex = body.IndexOfAny( new[] { ':', '@' } );
		var name = suffixIndex < 0 ? body : body[..suffixIndex];
		var suffix = suffixIndex < 0 ? "" : body[suffixIndex..];

		if ( TryTranslateDirect( name, out var translated ) )
			return $"{prefix}{translated}{suffix}";

		CollectMissingText( name );
		return value;
	}

	static void CollectMissingText( string value )
	{
		if ( !SboxTranslationFiles.CollectMissingTexts )
			return;

		value = NormalizeMissingText( value );
		if ( !ShouldCollectMissingText( value ) )
			return;

		lock ( MissingLock )
		{
			GetMissingSetForCurrentLanguage().Add( value );
		}
	}

	static bool TryTranslateDirect( string value, out string translated )
	{
		if ( SboxTranslationFiles.TryTranslateUi( value, out translated ) )
			return true;

		if ( SboxTranslationFiles.UseBuiltinChineseFallback && Exact.TryGetValue( value, out translated ) )
			return true;

		translated = null;
		return false;
	}

	static string ReplaceTranslatedSegment( string value, string source )
	{
		if ( !TryTranslateDirect( source, out var translated ) )
			return value;

		return value.Replace( source, translated, StringComparison.Ordinal );
	}

	static string ReplaceTranslatedHtmlType( string value, string typeName )
	{
		if ( !TryTranslateDirect( typeName, out var translated ) )
			return value;

		return value.Replace( $">{typeName}</span>", $">{translated}</span>", StringComparison.Ordinal );
	}

	static SortedSet<string> GetMissingSetForCurrentLanguage()
	{
		var language = SboxTranslationFiles.CurrentLanguage;
		if ( !MissingByLanguage.TryGetValue( language, out var set ) )
		{
			set = new SortedSet<string>( StringComparer.OrdinalIgnoreCase );
			MissingByLanguage[language] = set;
		}

		return set;
	}

	static string NormalizeMissingText( string value )
	{
		value = value?.Trim();
		if ( string.IsNullOrEmpty( value ) )
			return value;

		if ( value.StartsWith( "[missing]", StringComparison.OrdinalIgnoreCase ) )
			value = value["[missing]".Length..].Trim();

		value = value.TrimStart( '⌕' ).Trim();

		var tabIndex = value.IndexOf( '\t' );
		if ( tabIndex >= 0 )
			value = value[..tabIndex].Trim();

		return value;
	}

	static string NormalizeRichTextLookup( string value )
	{
		var lines = value
			.Replace( "\r\n", "\n", StringComparison.Ordinal )
			.Replace( '\r', '\n' )
			.Split( '\n' )
			.Select( x => x.Trim() )
			.ToList();

		while ( lines.Count > 0 && string.IsNullOrEmpty( lines[0] ) )
		{
			lines.RemoveAt( 0 );
		}

		while ( lines.Count > 0 && string.IsNullOrEmpty( lines[^1] ) )
		{
			lines.RemoveAt( lines.Count - 1 );
		}

		return string.Join( "\n", lines );
	}

	static bool ShouldCollectMissingText( string value )
	{
		if ( string.IsNullOrWhiteSpace( value ) )
			return false;

		if ( value.Length < 2 || value.Length > 80 )
			return false;

		if ( value.StartsWith( "[missing]", StringComparison.OrdinalIgnoreCase ) ||
			value.StartsWith( "Missing translation texts", StringComparison.OrdinalIgnoreCase ) )
		{
			return false;
		}

		if ( value.StartsWith( "[" ) && value.EndsWith( "]" ) )
			return false;

		if ( value.Any( c => c >= 0x4e00 && c <= 0x9fff ) )
			return false;

		if ( !value.Any( char.IsLetter ) || !value.Any( c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' ) )
			return false;

		if ( value.Contains( "://", StringComparison.Ordinal ) || value.Contains( '\\' ) )
			return false;

		var lower = value.ToLowerInvariant();
		if ( lower.StartsWith( "shared with ", StringComparison.Ordinal ) )
			return false;

		if ( lower.StartsWith( "collection took ", StringComparison.Ordinal ) ||
			lower.StartsWith( "instance queue:", StringComparison.Ordinal ) ||
			lower.StartsWith( "static fields:", StringComparison.Ordinal ) ||
			lower.StartsWith( "error loading resource file ", StringComparison.Ordinal ) )
		{
			return false;
		}

		if ( char.IsDigit( value[0] ) && (lower.Contains( "mb " ) || lower.Contains( "gb " ) || lower.EndsWith( " textures" )) )
			return false;

		if ( lower.EndsWith( "fps" ) && value.Take( value.Length - 3 ).All( char.IsDigit ) )
			return false;

		if ( lower.EndsWith( "x" ) && value.Take( value.Length - 1 ).All( char.IsDigit ) )
			return false;

		if ( lower.Length > 1 && lower[0] == 'v' && lower.Skip( 1 ).All( char.IsDigit ) )
			return false;

		if ( !value.Contains( ' ' ) && value.Any( char.IsDigit ) )
			return false;

		if ( !value.Contains( ' ' ) && value.Contains( '-' ) )
			return false;

		if ( value.Contains( '_' ) && !value.Contains( ' ' ) )
			return false;

		if ( value.Contains( '_' ) && lower.Contains( "error" ) )
			return false;

		if ( value.EndsWith( "ToolBar", StringComparison.Ordinal ) )
			return false;

		if ( value.Contains( '.' ) && !value.Contains( ' ' ) )
			return false;

		if ( value.Count( c => c == '/' ) > 1 )
			return false;

		if ( value.Any( c => c is '{' or '}' or '<' or '>' ) )
			return false;

		if ( lower.EndsWith( ".cs" ) || lower.EndsWith( ".dll" ) || lower.EndsWith( ".json" ) ||
			lower.EndsWith( ".scene" ) || lower.EndsWith( ".vmdl" ) || lower.EndsWith( ".vmat" ) ||
			lower.EndsWith( ".png" ) || lower.EndsWith( ".jpg" ) )
		{
			return false;
		}

		return true;
	}
}

