using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Editor;

static class SboxTranslationFiles
{
	// Translation files are reloaded at runtime, so keep file loading isolated from Harmony patching.
	static readonly Sandbox.Diagnostics.Logger Logger = new( "SboxTranslationFiles" );
	static readonly object LoadLock = new();
	static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

	static bool loaded;
	static TranslationSettings settings = new();
	static TranslationCatalog currentCatalog = TranslationCatalog.Empty;
	static TranslationCatalog fallbackCatalog = TranslationCatalog.Empty;

	public static string CurrentLanguage
	{
		get
		{
			EnsureLoaded();
			return settings.CurrentLanguage;
		}
	}

	public static bool CollectMissingTexts
	{
		get
		{
			EnsureLoaded();
			return settings.CollectMissingTexts;
		}
	}

	public static bool UseBuiltinChineseFallback
	{
		get
		{
			EnsureLoaded();
			return IsChineseLocale( settings.CurrentLanguage ) || IsChineseLocale( settings.FallbackLanguage );
		}
	}

	public static void Reload()
	{
		lock ( LoadLock )
		{
			loaded = false;
			settings = new();
			currentCatalog = TranslationCatalog.Empty;
			fallbackCatalog = TranslationCatalog.Empty;
		}
	}

	public static bool TryTranslateUi( string value, out string translated )
	{
		EnsureLoaded();
		value = NormalizeText( value );
		return currentCatalog.Ui.TryGetValue( value, out translated ) ||
			fallbackCatalog.Ui.TryGetValue( value, out translated );
	}

	public static bool TryTranslateApi( string value, out string translated )
	{
		EnsureLoaded();
		value = NormalizeText( value );
		return currentCatalog.Api.TryGetValue( value, out translated ) ||
			fallbackCatalog.Api.TryGetValue( value, out translated );
	}

	static void EnsureLoaded()
	{
		if ( loaded )
			return;

		lock ( LoadLock )
		{
			if ( loaded )
				return;

			loaded = true;

			try
			{
				// Prefer project-local files so the plugin stays self-contained when shared between projects.
				var projectRoot = Sandbox.Project.Current?.GetRootPath();
				if ( string.IsNullOrWhiteSpace( projectRoot ) )
					projectRoot = Directory.GetCurrentDirectory();

				var roots = GetTranslationRoots( projectRoot ).ToArray();
				settings = LoadSettings( roots );
				currentCatalog = LoadCatalog( roots, settings.CurrentLanguage );
				fallbackCatalog = string.Equals( settings.CurrentLanguage, settings.FallbackLanguage, StringComparison.OrdinalIgnoreCase )
					? TranslationCatalog.Empty
					: LoadCatalog( roots, settings.FallbackLanguage );

				Logger.Info( $"Loaded translations. Current={settings.CurrentLanguage}, Fallback={settings.FallbackLanguage}, UI={currentCatalog.Ui.Count}, API={currentCatalog.Api.Count}" );
			}
			catch ( Exception e )
			{
				Logger.Warning( e, $"Failed to load translation files: {e.Message}" );
			}
		}
	}

	static TranslationSettings LoadSettings( IReadOnlyList<string> roots )
	{
		var path = roots
			.Select( root => Path.Combine( root, "Localization", "settings.json" ) )
			.FirstOrDefault( File.Exists );
		if ( string.IsNullOrWhiteSpace( path ) )
			return new TranslationSettings();

		return JsonSerializer.Deserialize<TranslationSettings>( File.ReadAllText( path ), JsonOptions ) ?? new TranslationSettings();
	}

	static TranslationCatalog LoadCatalog( IReadOnlyList<string> roots, string locale )
	{
		return new TranslationCatalog(
			LoadUiTranslations( roots, locale ),
			LoadApiTranslations( roots, locale ) );
	}

	static Dictionary<string, string> LoadUiTranslations( IReadOnlyList<string> roots, string locale )
	{
		var map = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var path in EnumerateLocaleFiles( roots, "ui", locale, includeLegacyShortName: false ) )
		{
			foreach ( var pair in JsonSerializer.Deserialize<Dictionary<string, string>>( File.ReadAllText( path ), JsonOptions ) ?? new Dictionary<string, string>() )
			{
				var key = NormalizeText( pair.Key );
				var value = NormalizeText( pair.Value );
				if ( string.IsNullOrWhiteSpace( key ) || string.IsNullOrWhiteSpace( value ) )
					continue;

				map[key] = value;
			}
		}

		return map;
	}

	static Dictionary<string, string> LoadApiTranslations( IReadOnlyList<string> roots, string locale )
	{
		var apiPath = FindApiJsonPath( roots );
		var map = new Dictionary<string, string>( StringComparer.Ordinal );
		var fullTranslationPaths = EnumerateFullApiTranslationFiles( roots, locale ).ToArray();

		if ( string.IsNullOrWhiteSpace( apiPath ) )
		{
			if ( fullTranslationPaths.Length > 0 )
			{
				Logger.Warning( $"API translation files for {locale} exist, but api.json was not found." );
			}

			return map;
		}

		foreach ( var fullTranslationPath in fullTranslationPaths )
		{
			MergeFullApiTranslation( apiPath, fullTranslationPath, map );
		}

		return map;
	}

	static IEnumerable<string> EnumerateFullApiTranslationFiles( IReadOnlyList<string> roots, string locale )
	{
		foreach ( var path in EnumerateLocaleFiles( roots, "api.full", locale, includeLegacyShortName: false ) )
		{
			yield return path;
		}

		foreach ( var root in roots )
		{
			var legacy = Path.Combine( root, "Localization", "api_translated.json" );
			if ( File.Exists( legacy ) )
				yield return legacy;
		}
	}

	static IEnumerable<string> EnumerateLocaleFiles( IReadOnlyList<string> roots, string prefix, string locale, bool includeLegacyShortName )
	{
		var seen = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		foreach ( var root in roots )
		{
			foreach ( var candidateLocale in EnumerateLocaleCandidates( locale ) )
			{
				var path = Path.Combine( root, "Localization", $"{prefix}.{candidateLocale}.json" );
				if ( File.Exists( path ) && seen.Add( path ) )
					yield return path;
			}

			if ( includeLegacyShortName )
			{
				var shortLocale = GetShortLocale( locale );
				if ( !string.IsNullOrWhiteSpace( shortLocale ) )
				{
					var legacyPath = Path.Combine( root, "Localization", $"{prefix}.{shortLocale}.json" );
					if ( File.Exists( legacyPath ) && seen.Add( legacyPath ) )
						yield return legacyPath;
				}
			}
		}
	}

	static IEnumerable<string> EnumerateLocaleCandidates( string locale )
	{
		if ( string.IsNullOrWhiteSpace( locale ) )
			yield break;

		var shortLocale = GetShortLocale( locale );
		if ( !string.IsNullOrWhiteSpace( shortLocale ) && !string.Equals( shortLocale, locale, StringComparison.OrdinalIgnoreCase ) )
			yield return shortLocale;

		yield return locale;
	}

	static string GetShortLocale( string locale )
	{
		if ( string.IsNullOrWhiteSpace( locale ) )
			return null;

		var separatorIndex = locale.IndexOfAny( new[] { '-', '_' } );
		return separatorIndex > 0 ? locale[..separatorIndex] : locale;
	}

	static bool IsChineseLocale( string locale )
	{
		var shortLocale = GetShortLocale( locale );
		return string.Equals( shortLocale, "zh", StringComparison.OrdinalIgnoreCase );
	}

	static string FindApiJsonPath( IReadOnlyList<string> roots )
	{
		var candidates = roots
			.SelectMany( root => new[]
			{
				Path.Combine( root, "Localization", "api.json" ),
				Path.Combine( root, "Assets", "Localization", "api.json" )
			} )
			.Append( Path.Combine( Environment.GetFolderPath( Environment.SpecialFolder.DesktopDirectory ), "api.json" ) )
			.ToArray();

		return candidates.FirstOrDefault( File.Exists );
	}

	static IEnumerable<string> GetTranslationRoots( string projectRoot )
	{
		var assemblyDirectory = Path.GetDirectoryName( typeof( SboxTranslationFiles ).Assembly.Location ) ?? AppContext.BaseDirectory;
		var outputRoot = Path.Combine( assemblyDirectory, ".vs", "output" );
		var seen = new HashSet<string>( StringComparer.OrdinalIgnoreCase );

		if ( seen.Add( projectRoot ) )
			yield return projectRoot;

		// Keep consuming-project library folders first so teams can override defaults without touching the
		// shared library. Enumerating Libraries avoids depending on a specific extracted folder name.
		foreach ( var root in EnumerateChildDirectories( Path.Combine( projectRoot, "Libraries" ) ) )
		{
			if ( seen.Add( root ) )
				yield return root;
		}

		// When the patch ships as a library, translation files may live beside the compiled library output
		// or inside .vs/output/<library>. Keep these as fallbacks so the packaged library stays self-contained.
		foreach ( var root in EnumerateChildDirectories( assemblyDirectory ) )
		{
			if ( seen.Add( root ) )
				yield return root;
		}

		foreach ( var root in EnumerateChildDirectories( outputRoot ) )
		{
			if ( seen.Add( root ) )
				yield return root;
		}
	}

	static IEnumerable<string> EnumerateChildDirectories( string parent )
	{
		if ( !Directory.Exists( parent ) )
			yield break;

		foreach ( var directory in Directory.GetDirectories( parent ) )
		{
			yield return directory;
		}
	}

	static void MergeFullApiTranslation( string sourceApiPath, string translatedApiPath, Dictionary<string, string> map )
	{
		var sourceTexts = LoadApiTextsByDocId( sourceApiPath );
		var translatedTexts = LoadApiTextsByDocId( translatedApiPath );
		var matched = 0;

		foreach ( var pair in sourceTexts )
		{
			if ( !translatedTexts.TryGetValue( pair.Key, out var translated ) )
				continue;

			matched++;
			// We match by DocId once, then build a flat source->translation lookup for the live UI hooks.
			AddMappedText( map, pair.Value.Name, translated.Name );
			AddMappedText( map, pair.Value.Title, translated.Title );
			AddMappedText( map, pair.Value.Description, translated.Description );
			AddMappedText( map, pair.Value.Summary, translated.Summary );
		}

		Logger.Info( $"Loaded full API translation from {Path.GetFileName( translatedApiPath )}: matched {matched} doc ids, produced {map.Count} mapped strings." );
	}

	static Dictionary<string, ApiDocTexts> LoadApiTextsByDocId( string path )
	{
		using var document = JsonDocument.Parse( File.ReadAllText( path ) );
		var map = new Dictionary<string, ApiDocTexts>( StringComparer.Ordinal );
		CollectApiTextsByDocId( document.RootElement, map );
		return map;
	}

	static void CollectApiTextsByDocId( JsonElement element, Dictionary<string, ApiDocTexts> map )
	{
		if ( element.ValueKind == JsonValueKind.Object )
		{
			if ( element.TryGetProperty( "DocId", out var docIdElement ) &&
				docIdElement.ValueKind == JsonValueKind.String &&
				docIdElement.GetString() is { } docId )
			{
				map[docId] = ExtractApiDocTexts( element );
			}

			foreach ( var property in element.EnumerateObject() )
			{
				CollectApiTextsByDocId( property.Value, map );
			}
		}
		else if ( element.ValueKind == JsonValueKind.Array )
		{
			foreach ( var child in element.EnumerateArray() )
			{
				CollectApiTextsByDocId( child, map );
			}
		}
	}

	static ApiDocTexts ExtractApiDocTexts( JsonElement element )
	{
		var texts = new ApiDocTexts();

		if ( element.TryGetProperty( "Name", out var name ) && name.ValueKind == JsonValueKind.String )
		{
			texts.Name = name.GetString();
		}

		if ( element.TryGetProperty( "Documentation", out var documentation ) &&
			documentation.ValueKind == JsonValueKind.Object &&
			documentation.TryGetProperty( "Summary", out var summary ) )
		{
			texts.Summary = summary.GetString();
		}

		if ( element.TryGetProperty( "Attributes", out var attributes ) && attributes.ValueKind == JsonValueKind.Array )
		{
			foreach ( var attribute in attributes.EnumerateArray() )
			{
				if ( !attribute.TryGetProperty( "FullName", out var fullNameElement ) )
					continue;

				var fullName = fullNameElement.GetString();
				var firstArgument = GetFirstConstructorArgument( attribute );
				if ( string.IsNullOrWhiteSpace( firstArgument ) )
					continue;

				if ( string.Equals( fullName, "TitleAttribute", StringComparison.Ordinal ) )
				{
					texts.Title = firstArgument;
				}
				else if ( string.Equals( fullName, "DescriptionAttribute", StringComparison.Ordinal ) )
				{
					texts.Description = firstArgument;
				}
			}
		}

		return texts;
	}

	static void AddMappedText( Dictionary<string, string> map, string source, string translated )
	{
		source = NormalizeText( source );
		translated = NormalizeText( translated );

		if ( string.IsNullOrWhiteSpace( source ) || string.IsNullOrWhiteSpace( translated ) )
			return;

		if ( string.Equals( source, translated, StringComparison.Ordinal ) )
			return;

		map[source] = translated;
	}

	static string GetFirstConstructorArgument( JsonElement attribute )
	{
		if ( !attribute.TryGetProperty( "ConstructorArguments", out var arguments ) || arguments.ValueKind != JsonValueKind.Array )
			return null;

		foreach ( var argument in arguments.EnumerateArray() )
		{
			return argument.ValueKind == JsonValueKind.String ? argument.GetString() : null;
		}

		return null;
	}

	static string NormalizeText( string value )
	{
		return value?.Replace( "\r\n", "\n" ).Trim();
	}

	sealed class TranslationCatalog
	{
		public static readonly TranslationCatalog Empty = new( new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase ), new Dictionary<string, string>( StringComparer.Ordinal ) );

		public TranslationCatalog( Dictionary<string, string> ui, Dictionary<string, string> api )
		{
			Ui = ui;
			Api = api;
		}

		public Dictionary<string, string> Ui { get; }
		public Dictionary<string, string> Api { get; }
	}

	sealed class TranslationSettings
	{
		public string CurrentLanguage { get; set; } = "zh-CN";
		public string FallbackLanguage { get; set; } = "en";
		public bool CollectMissingTexts { get; set; } = true;
	}

	sealed class ApiDocTexts
	{
		public string Name { get; set; }
		public string Title { get; set; }
		public string Summary { get; set; }
		public string Description { get; set; }
	}
}
