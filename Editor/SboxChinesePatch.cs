using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Editor;

public static class SboxChinesePatch
{
	const string HarmonyId = "local.translation.sbox-editor-zh-cn";
	// This project ships the library under Libraries/slfy.translationtochinese, so path probing must
	// use the actual folder name instead of the original template name.
	const string LibraryFolderName = "slfy.translationtochinese";

	static readonly Sandbox.Diagnostics.Logger Logger = new( "SboxChinesePatch" );
	static bool installed;
	static int refreshFrames;
	static bool restartQueued;
	static bool restartingUi;
	static readonly HashSet<MenuBar> RebuiltMenuBars = new( ReferenceEqualityComparer.Instance );

	[EditorEvent.Frame]
	static void InstallOnFrame()
	{
		if ( installed )
		{
			if ( refreshFrames > 0 )
			{
				refreshFrames--;
				ApplyExistingUi();
			}

			if ( restartQueued )
			{
				RestartEditorUi();
			}

			return;
		}

		Install();
	}

	[EditorEvent.Hotload]
	static void InstallOnHotload()
	{
		installed = false;
		Install();
	}

	[Menu( "Editor", "Translation/Apply Chinese Patch", "translate" )]
	public static void Install()
	{
		if ( installed )
			return;

		installed = true;
		SboxTranslationFiles.Reload();

		try
		{
			var harmonyAssembly = LoadHarmonyAssembly();
			var harmonyType = harmonyAssembly.GetType( "HarmonyLib.Harmony", true );
			var harmonyMethodType = harmonyAssembly.GetType( "HarmonyLib.HarmonyMethod", true );
			var harmony = Activator.CreateInstance( harmonyType, HarmonyId );

			UnpatchExisting( harmonyType, harmony );

			var prefix = typeof( SboxChinesePatch ).GetMethod( nameof( TranslateStringArgument ), BindingFlags.Static | BindingFlags.NonPublic );
			var harmonyMethod = Activator.CreateInstance( harmonyMethodType, prefix );

			// Setter hooks handle the common widget text paths that are created after the plugin loads.
			PatchSetter( harmonyType, harmony, harmonyMethod, typeof( Button ), nameof( Button.Text ) );
			PatchSetter( harmonyType, harmony, harmonyMethod, typeof( Label ), nameof( Label.Text ) );
			PatchSetter( harmonyType, harmony, harmonyMethod, typeof( Checkbox ), nameof( Checkbox.Text ) );
			PatchSetter( harmonyType, harmony, harmonyMethod, typeof( LineEdit ), nameof( LineEdit.PlaceholderText ) );
			PatchSetter( harmonyType, harmony, harmonyMethod, typeof( Option ), nameof( Option.Text ) );
			PatchSetter( harmonyType, harmony, harmonyMethod, typeof( Menu ), nameof( Menu.Title ) );
			PatchSetter( harmonyType, harmony, harmonyMethod, typeof( Widget ), nameof( Widget.WindowTitle ) );
			PatchSetter( harmonyType, harmony, harmonyMethod, typeof( Widget ), nameof( Widget.ToolTip ) );
			PatchSetter( harmonyType, harmony, harmonyMethod, typeof( Option ), nameof( Option.ToolTip ) );
			PatchSetter( harmonyType, harmony, harmonyMethod, typeof( Option ), nameof( Option.StatusTip ) );
			PatchSetter( harmonyType, harmony, harmonyMethod, typeof( AdvancedDropdownItem ), nameof( AdvancedDropdownItem.Title ) );
			PatchSetter( harmonyType, harmony, harmonyMethod, typeof( AdvancedDropdownItem ), nameof( AdvancedDropdownItem.Description ) );
			PatchSetter( harmonyType, harmony, harmonyMethod, typeof( AdvancedDropdownItem ), nameof( AdvancedDropdownItem.Tooltip ) );
			PatchSetter( harmonyType, harmony, harmonyMethod, typeof( AdvancedDropdownWidget ), nameof( AdvancedDropdownWidget.RootTitle ) );
			PatchSetter( harmonyType, harmony, harmonyMethod, typeof( AdvancedDropdownWidget ), nameof( AdvancedDropdownWidget.SearchPlaceholderText ) );
			PatchSetter( harmonyType, harmony, harmonyMethod, typeof( AdvancedDropdownWidget.ItemEntry ), nameof( AdvancedDropdownWidget.ItemEntry.Text ) );
			PatchSetter( harmonyType, harmony, harmonyMethod, typeof( AdvancedDropdownWidget.CategoryEntry ), nameof( AdvancedDropdownWidget.CategoryEntry.Category ) );

			var firstArgumentPrefix = typeof( SboxChinesePatch ).GetMethod( nameof( TranslateFirstStringArgument ), BindingFlags.Static | BindingFlags.NonPublic );
			var firstArgumentHarmonyMethod = Activator.CreateInstance( harmonyMethodType, firstArgumentPrefix );

			// Menus and native Qt actions often bypass widget setters, so they need their own first-argument hooks.
			PatchMenuBarAddMenuMethods( harmonyType, harmony, firstArgumentHarmonyMethod );
			PatchStringFirstArgumentMethods( harmonyType, harmony, firstArgumentHarmonyMethod, typeof( MenuBar ), nameof( MenuBar.AddOption ) );
			PatchStringFirstArgumentMethods( harmonyType, harmony, firstArgumentHarmonyMethod, typeof( MenuBar ), nameof( MenuBar.FindOrCreateMenu ) );
			PatchStringFirstArgumentMethods( harmonyType, harmony, firstArgumentHarmonyMethod, typeof( Menu ), nameof( Menu.AddOption ) );
			PatchStringFirstArgumentMethods( harmonyType, harmony, firstArgumentHarmonyMethod, typeof( Menu ), nameof( Menu.FindOrCreateMenu ) );
			PatchStringFirstArgumentMethods( harmonyType, harmony, firstArgumentHarmonyMethod, typeof( Menu ), nameof( Menu.AddHeading ) );
			PatchNativeStringMethod( harmonyType, harmony, firstArgumentHarmonyMethod, "Native.QAction", "setText" );
			PatchNativeStringMethod( harmonyType, harmony, firstArgumentHarmonyMethod, "Native.CAction", "setText" );
			PatchNativeStringMethod( harmonyType, harmony, firstArgumentHarmonyMethod, "Native.QMenu", "setTitle" );
			PatchNativeStringMethod( harmonyType, harmony, firstArgumentHarmonyMethod, "Native.QMenu", "setWindowTitle" );
			PatchNativeStringMethod( harmonyType, harmony, firstArgumentHarmonyMethod, "Native.QWidget", "setToolTip" );
			PatchNativeStringMethod( harmonyType, harmony, firstArgumentHarmonyMethod, "Native.QLineEdit", "setToolTip" );
			PatchNativeStringMethod( harmonyType, harmony, firstArgumentHarmonyMethod, "Native.QLabel", "setToolTip" );
			PatchNativeStringMethod( harmonyType, harmony, firstArgumentHarmonyMethod, "Native.QFrame", "setToolTip" );

			var resultPostfix = typeof( SboxChinesePatch ).GetMethod( nameof( TranslateStringResult ), BindingFlags.Static | BindingFlags.NonPublic );
			var resultHarmonyMethod = Activator.CreateInstance( harmonyMethodType, resultPostfix );

			var controlSheetToolTipPostfix = typeof( SboxChinesePatch ).GetMethod( nameof( TranslateControlSheetToolTipResult ), BindingFlags.Static | BindingFlags.NonPublic );
			var controlSheetToolTipHarmonyMethod = Activator.CreateInstance( harmonyMethodType, controlSheetToolTipPostfix );
			PatchStaticStringResultMethod( harmonyType, harmony, controlSheetToolTipHarmonyMethod, typeof( ControlSheetFormatter ), nameof( ControlSheetFormatter.GetPropertyToolTip ) );

			// Reflection metadata powers a lot of inspector text, so patch the getters instead of every caller.
			PatchSerializedPropertyStringGetters( harmonyType, harmony, resultHarmonyMethod, GetControlSheetSerializedPropertyType() );
			PatchMemberDescriptionStringGetters( harmonyType, harmony, resultHarmonyMethod );
			PatchTypeDescriptionStringGetters( harmonyType, harmony, resultHarmonyMethod );

			refreshFrames = 120;
			ApplyExistingUi();
			QueueRestartEditorUi();

			Logger.Info( $"Sbox Chinese Patch installed. Language: {SboxTranslationFiles.CurrentLanguage}" );
		}
		catch ( Exception e )
		{
			installed = false;
			Logger.Warning( e, $"Sbox Chinese Patch failed to install: {e.Message}" );
		}
	}

	[Menu( "Editor", "Translation/Test Chinese Dialog", "translate" )]
	public static void TestDialog()
	{
		EditorUtility.DisplayDialog( "Sbox Chinese Patch", "If this dialog has Chinese buttons or translated title later, the patch path is active." );
	}

	[Menu( "Editor", "Translation/Print Missing Texts", "list" )]
	public static void PrintMissingTexts()
	{
		var missing = SboxChineseDictionary.GetMissingTexts();
		if ( missing.Length == 0 )
		{
			Logger.Info( "No missing translation texts collected." );
			return;
		}

		Logger.Info( $"Missing translation texts for {SboxTranslationFiles.CurrentLanguage} ({missing.Length}):" );
		foreach ( var text in missing )
		{
			Logger.Info( $"[missing] {text}" );
		}
	}

	[Menu( "Editor", "Translation/Clear Missing Texts", "delete" )]
	public static void ClearMissingTexts()
	{
		SboxChineseDictionary.ClearMissingTexts();
		Logger.Info( "Missing translation texts cleared." );
	}

	[Menu( "Editor", "Translation/Reload Translation Files", "refresh" )]
	public static void ReloadTranslationFiles()
	{
		SboxTranslationFiles.Reload();
		refreshFrames = 120;
		ApplyExistingUi();
		QueueRestartEditorUi();
		Logger.Info( $"Translation files reloaded. Language: {SboxTranslationFiles.CurrentLanguage}" );
	}

	[Menu( "Editor", "Translation/Debug/Dump Focus Window Text Widgets", "search" )]
	public static void DumpFocusWindowTextWidgets()
	{
		var focus = Application.FocusWidget;
		if ( focus is null || !focus.IsValid )
		{
			Logger.Warning( "No focused widget." );
			return;
		}

		var window = focus.GetWindow() ?? focus;
		Logger.Info( $"Dumping focus window: {DescribeWidget( window )}" );

		foreach ( var line in DumpWidgetTree( window ) )
		{
			Logger.Info( line );
		}
	}

	[Menu( "Editor", "Translation/Debug/Dump Hovered Widget Chain", "search" )]
	public static void DumpHoveredWidgetChain()
	{
		var hovered = Application.HoveredWidget;
		if ( hovered is null || !hovered.IsValid )
		{
			Logger.Warning( "No hovered widget." );
			return;
		}

		Logger.Info( $"Hovered widget: {DescribeWidget( hovered )}" );
		var current = hovered;
		var depth = 0;
		while ( current is not null && current.IsValid && depth < 20 )
		{
			Logger.Info( $"{new string( ' ', depth * 2 )}{DescribeWidget( current )}" );
			current = current.Parent;
			depth++;
		}
	}

	[Menu( "Editor", "Translation/Restart Editor UI", "refresh" )]
	public static void QueueRestartEditorUi()
	{
		if ( restartQueued || restartingUi )
			return;

		restartQueued = true;
	}

	static Assembly LoadHarmonyAssembly()
	{
		// In project mode Harmony might already be loaded by another editor assembly, so reuse it first.
		var loaded = AppDomain.CurrentDomain.GetAssemblies()
			.FirstOrDefault( x => string.Equals( x.GetName().Name, "0Harmony", StringComparison.OrdinalIgnoreCase ) );

		if ( loaded is not null )
			return loaded;

		try
		{
			// If the loader can resolve the reference normally, avoid any manual file probing.
			return Assembly.Load( "0Harmony" );
		}
		catch
		{
		}

		var projectRoot = Sandbox.Project.Current?.GetRootPath();
		if ( string.IsNullOrWhiteSpace( projectRoot ) )
			projectRoot = Directory.GetCurrentDirectory();

		var projectLibraryRoot = Path.Combine( projectRoot, "Libraries", LibraryFolderName );
		var assemblyDirectory = Path.GetDirectoryName( typeof( SboxChinesePatch ).Assembly.Location ) ?? AppContext.BaseDirectory;
		var outputRoot = Path.Combine( assemblyDirectory, ".vs", "output" );
		var baseDirectoryOutputRoot = Path.Combine( AppContext.BaseDirectory, ".vs", "output" );

		// Library projects do not always run from the project root. Depending on how s&box bootstraps the editor,
		// Harmony can live in the consuming project's Libraries folder, beside the compiled output, or directly in
		// .vs/output when the editor project copied content there. Probe all explicit library locations first so
		// future project copies do not depend on whichever implicit current directory happens to be active.
		var candidates = new[]
		{
			Path.Combine( projectRoot, "Assets", "3rd", "harmony", "0Harmony.dll" ),
			Path.Combine( projectLibraryRoot, "Assets", "3rd", "harmony", "0Harmony.dll" ),
			Path.Combine( assemblyDirectory, "0Harmony.dll" ),
			Path.Combine( outputRoot, "0Harmony.dll" ),
			Path.Combine( assemblyDirectory, LibraryFolderName, "Assets", "3rd", "harmony", "0Harmony.dll" ),
			Path.Combine( AppContext.BaseDirectory, "0Harmony.dll" ),
			Path.Combine( baseDirectoryOutputRoot, "0Harmony.dll" ),
			Path.Combine( AppContext.BaseDirectory, LibraryFolderName, "Assets", "3rd", "harmony", "0Harmony.dll" )
		};

		var path = candidates.FirstOrDefault( File.Exists );
		if ( string.IsNullOrWhiteSpace( path ) )
			throw new FileNotFoundException( "Could not find 0Harmony.dll.", string.Join( "; ", candidates ) );

		return Assembly.LoadFrom( path );
	}

	static void UnpatchExisting( Type harmonyType, object harmony )
	{
		var unpatch = harmonyType.GetMethods( BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance )
			.FirstOrDefault( x =>
				x.Name == "UnpatchAll" &&
				x.GetParameters() is { Length: 1 } p &&
				p[0].ParameterType == typeof( string ) );

		unpatch?.Invoke( unpatch.IsStatic ? null : harmony, new object[] { HarmonyId } );
	}

	static void PatchSetter( Type harmonyType, object harmony, object harmonyMethod, Type targetType, string propertyName )
	{
		var setter = targetType.GetProperty( propertyName, BindingFlags.Public | BindingFlags.Instance )?.SetMethod;
		if ( setter is null )
			throw new MissingMethodException( targetType.FullName, $"set_{propertyName}" );

		var patch = harmonyType.GetMethods( BindingFlags.Public | BindingFlags.Instance )
			.First( x =>
			{
				if ( x.Name != "Patch" )
					return false;

				var parameters = x.GetParameters();
				return parameters.Length >= 2 && parameters[0].ParameterType == typeof( MethodBase );
			} );

		var args = patch.GetParameters().Select( _ => (object)null ).ToArray();
		args[0] = setter;
		args[1] = harmonyMethod;
		patch.Invoke( harmony, args );
	}

	static void PatchStringFirstArgumentMethods( Type harmonyType, object harmony, object harmonyMethod, Type targetType, string methodName )
	{
		foreach ( var method in targetType.GetMethods( BindingFlags.Public | BindingFlags.Instance ) )
		{
			if ( method.Name != methodName )
				continue;

			var parameters = method.GetParameters();
			if ( parameters.Length == 0 || parameters[0].ParameterType != typeof( string ) )
				continue;

			PatchMethod( harmonyType, harmony, harmonyMethod, method );
		}
	}

	static void PatchMenuBarAddMenuMethods( Type harmonyType, object harmony, object harmonyMethod )
	{
		foreach ( var method in typeof( MenuBar ).GetMethods( BindingFlags.Public | BindingFlags.Instance ) )
		{
			if ( method.Name != nameof( MenuBar.AddMenu ) )
				continue;

			var parameters = method.GetParameters();
			if ( parameters.Length != 1 || parameters[0].ParameterType != typeof( string ) )
				continue;

			PatchMethod( harmonyType, harmony, harmonyMethod, method );
		}
	}

	static void PatchNativeStringMethod( Type harmonyType, object harmony, object harmonyMethod, string typeName, string methodName )
	{
		try
		{
			var type = typeof( Widget ).Assembly.GetType( typeName, false );
			if ( type is null )
				return;

			foreach ( var method in type.GetMethods( BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic ) )
			{
				if ( method.Name != methodName )
					continue;

				var parameters = method.GetParameters();
				if ( parameters.Length == 1 && parameters[0].ParameterType == typeof( string ) )
				{
					PatchMethod( harmonyType, harmony, harmonyMethod, method );
				}
			}
		}
		catch ( Exception e )
		{
			Logger.Warning( e, $"Skipping native patch {typeName}.{methodName}: {e.Message}" );
		}
	}

	static Type GetControlSheetSerializedPropertyType()
	{
		return typeof( ControlSheetFormatter )
			.GetMethods( BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static )
			.FirstOrDefault( x => x.Name == nameof( ControlSheetFormatter.GetPropertyToolTip ) )
			?.GetParameters()
			.FirstOrDefault()
			?.ParameterType;
	}

	static void PatchSerializedPropertyStringGetters( Type harmonyType, object harmony, object harmonyMethod, Type serializedPropertyType )
	{
		if ( serializedPropertyType is null )
			return;

		var patched = new HashSet<MethodBase>();

		foreach ( var assembly in AppDomain.CurrentDomain.GetAssemblies() )
		{
			Type[] types;

			try
			{
				types = assembly.GetTypes();
			}
			catch ( ReflectionTypeLoadException e )
			{
				types = e.Types.Where( x => x is not null ).ToArray();
			}
			catch ( Exception e )
			{
				Logger.Warning( e, $"Skipping assembly scan {assembly.GetName().Name}: {e.Message}" );
				continue;
			}

			foreach ( var type in types )
			{
				if ( !serializedPropertyType.IsAssignableFrom( type ) )
					continue;

				PatchStringGetter( harmonyType, harmony, harmonyMethod, type, "DisplayName", patched );
				PatchStringGetter( harmonyType, harmony, harmonyMethod, type, "Description", patched );
				PatchStringGetter( harmonyType, harmony, harmonyMethod, type, "GroupName", patched );
			}
		}
	}

	static void PatchTypeDescriptionStringGetters( Type harmonyType, object harmony, object harmonyMethod )
	{
		var patched = new HashSet<MethodBase>();
		PatchStringGetter( harmonyType, harmony, harmonyMethod, typeof( Sandbox.TypeDescription ), "Title", patched );
		PatchStringGetter( harmonyType, harmony, harmonyMethod, typeof( Sandbox.TypeDescription ), "Description", patched );
		PatchStringGetter( harmonyType, harmony, harmonyMethod, typeof( Sandbox.TypeDescription ), "Group", patched );
	}

	static void PatchMemberDescriptionStringGetters( Type harmonyType, object harmony, object harmonyMethod )
	{
		var patched = new HashSet<MethodBase>();
		PatchStringGetter( harmonyType, harmony, harmonyMethod, typeof( Sandbox.MemberDescription ), "Title", patched );
		PatchStringGetter( harmonyType, harmony, harmonyMethod, typeof( Sandbox.MemberDescription ), "Description", patched );
		PatchStringGetter( harmonyType, harmony, harmonyMethod, typeof( Sandbox.MemberDescription ), "Group", patched );
	}

	static void PatchStringGetter( Type harmonyType, object harmony, object harmonyMethod, Type targetType, string propertyName, HashSet<MethodBase> patched )
	{
		try
		{
			var getter = targetType.GetProperty( propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance )?.GetMethod;
			if ( getter is null || getter.ReturnType != typeof( string ) || !patched.Add( getter ) )
				return;

			PatchMethodPostfix( harmonyType, harmony, harmonyMethod, getter );
		}
		catch ( Exception e )
		{
			Logger.Warning( e, $"Skipping property patch {targetType.FullName}.{propertyName}: {e.Message}" );
		}
	}

	static void PatchStaticStringResultMethod( Type harmonyType, object harmony, object harmonyMethod, Type targetType, string methodName )
	{
		foreach ( var method in targetType.GetMethods( BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static ) )
		{
			if ( method.Name == methodName && method.ReturnType == typeof( string ) )
			{
				PatchMethodPostfix( harmonyType, harmony, harmonyMethod, method );
			}
		}
	}

	static void PatchMethod( Type harmonyType, object harmony, object harmonyMethod, MethodBase method )
	{
		var patch = GetHarmonyPatchMethod( harmonyType );

		var args = patch.GetParameters().Select( _ => (object)null ).ToArray();
		args[0] = method;
		args[1] = harmonyMethod;
		patch.Invoke( harmony, args );
	}

	static void PatchMethodPostfix( Type harmonyType, object harmony, object harmonyMethod, MethodBase method )
	{
		var patch = GetHarmonyPatchMethod( harmonyType );

		var args = patch.GetParameters().Select( _ => (object)null ).ToArray();
		args[0] = method;
		args[2] = harmonyMethod;
		patch.Invoke( harmony, args );
	}

	static MethodInfo GetHarmonyPatchMethod( Type harmonyType )
	{
		return harmonyType.GetMethods( BindingFlags.Public | BindingFlags.Instance )
			.First( x =>
			{
				if ( x.Name != "Patch" )
					return false;

				var parameters = x.GetParameters();
				return parameters.Length >= 3 && parameters[0].ParameterType == typeof( MethodBase );
			} );
	}

	static void TranslateStringArgument( ref string value )
	{
		value = SboxChineseDictionary.Translate( value );
	}

	static void TranslateFirstStringArgument( ref string __0 )
	{
		__0 = SboxChineseDictionary.Translate( __0 );
	}

	static void TranslateStringResult( ref string __result )
	{
		__result = SboxChineseDictionary.Translate( __result );
	}

	static void TranslateControlSheetToolTipResult( ref string __result )
	{
		__result = SboxChineseDictionary.TranslateControlSheetToolTip( __result );
	}

	static void ApplyExistingUi()
	{
		try
		{
			var current = typeof( EditorMainWindow ).GetField( "Current", BindingFlags.Static | BindingFlags.NonPublic )?.GetValue( null ) as EditorMainWindow;
			if ( current is null || !current.IsValid )
				return;

			current.WindowTitle = SboxChineseDictionary.Translate( current.WindowTitle );
			ApplyMenuBar( current.MenuBar );

			foreach ( var widget in current.GetDescendants<Widget>() )
			{
				widget.WindowTitle = SboxChineseDictionary.Translate( widget.WindowTitle );
				widget.ToolTip = SboxChineseDictionary.Translate( widget.ToolTip );

				if ( widget is MenuBar menuBar )
				{
					ApplyMenuBar( menuBar );
				}
				else if ( widget is Button button )
				{
					button.Text = SboxChineseDictionary.Translate( button.Text );
				}
				else if ( widget is Label label )
				{
					label.Text = SboxChineseDictionary.Translate( label.Text );
				}
				else if ( widget is Checkbox checkbox )
				{
					checkbox.Text = SboxChineseDictionary.Translate( checkbox.Text );
				}
				else if ( widget is LineEdit lineEdit )
				{
					lineEdit.PlaceholderText = SboxChineseDictionary.Translate( lineEdit.PlaceholderText );
				}
			}

			ApplyAllMenusFromObjectGraph( current );
		}
		catch ( Exception e )
		{
			Logger.Warning( e, $"Sbox Chinese Patch failed to refresh existing UI: {e.Message}" );
			refreshFrames = 0;
		}
	}

	static void RestartEditorUi()
	{
		restartQueued = false;

		if ( restartingUi )
			return;

		restartingUi = true;

		try
		{
			var oldWindow = typeof( EditorMainWindow ).GetField( "Current", BindingFlags.Static | BindingFlags.NonPublic )?.GetValue( null ) as EditorMainWindow;
			if ( oldWindow is null || !oldWindow.IsValid )
				return;

			oldWindow.SetVisible( false );
			oldWindow.SaveToStateCookie();

			RebuiltMenuBars.Clear();

			var newWindow = Activator.CreateInstance( typeof( EditorMainWindow ), nonPublic: true ) as EditorMainWindow;
			if ( newWindow is null )
				return;

			typeof( EditorMainWindow )
				.GetMethod( "Startup", BindingFlags.Instance | BindingFlags.NonPublic )
				?.Invoke( newWindow, null );

			oldWindow.Destroy();
			refreshFrames = 120;
			ApplyExistingUi();

			Logger.Info( "Sbox Chinese Patch restarted editor UI." );
		}
		catch ( Exception e )
		{
			Logger.Warning( e, $"Sbox Chinese Patch failed to restart editor UI: {e.Message}" );
		}
		finally
		{
			restartingUi = false;
		}
	}

	static void ApplyMenuBar( MenuBar menuBar )
	{
		if ( menuBar is null || !menuBar.IsValid )
			return;

		var menus = GetFieldList<Menu>( menuBar, "Menus" );
		var menuList = menus?.ToArray() ?? Array.Empty<Menu>();

		foreach ( var menu in menuList )
		{
			ApplyMenu( menu );
		}

		RebuildNativeMenuBar( menuBar, menuList );
	}

	static void ApplyMenu( Menu menu )
	{
		if ( menu is null || !menu.IsValid )
			return;

		menu.Title = SboxChineseDictionary.Translate( menu.Title );
		menu.ToolTip = SboxChineseDictionary.Translate( menu.ToolTip );
		SetNativeString( menu, "_menu", "setTitle", menu.Title );

		foreach ( var option in GetFieldList<Option>( menu, "Options" )?.ToArray() ?? Array.Empty<Option>() )
		{
			if ( option is null || !option.IsValid )
				continue;

			try
			{
				option.Text = SboxChineseDictionary.Translate( option.Text );
				option.ToolTip = SboxChineseDictionary.Translate( option.ToolTip );
				option.StatusTip = SboxChineseDictionary.Translate( option.StatusTip );
				SetNativeString( option, "_action", "setText", option.Text );
			}
			catch ( Exception e )
			{
				Logger.Warning( e, $"Sbox Chinese Patch skipped menu option: {e.Message}" );
			}
		}

		foreach ( var child in GetFieldList<Menu>( menu, "Menus" )?.ToArray() ?? Array.Empty<Menu>() )
		{
			ApplyMenu( child );
		}
	}

	static List<T> GetFieldList<T>( object target, string fieldName )
	{
		var type = target.GetType();
		FieldInfo field = null;

		while ( type is not null && field is null )
		{
			field = type.GetField( fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
			type = type.BaseType;
		}

		return field?.GetValue( target ) as List<T>;
	}

	static void SetNativeString( object target, string fieldName, string methodName, string value )
	{
		if ( string.IsNullOrEmpty( value ) )
			return;

		try
		{
			var field = target.GetType().GetField( fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
			var native = field?.GetValue( target );
			if ( native is null )
				return;

			var method = native.GetType().GetMethod( methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof( string ) }, null );
			method?.Invoke( native, new object[] { value } );
		}
		catch
		{
		}
	}

	static void RebuildNativeMenuBar( MenuBar menuBar, IReadOnlyList<Menu> menus )
	{
		if ( menus.Count == 0 || !RebuiltMenuBars.Add( menuBar ) )
			return;

		var menuBarField = typeof( MenuBar ).GetField( "_menubar", BindingFlags.Instance | BindingFlags.NonPublic );
		var nativeMenuBar = menuBarField?.GetValue( menuBar );
		if ( nativeMenuBar is null )
			return;

		var nativeMenuBarType = nativeMenuBar.GetType();
		var clear = nativeMenuBarType.GetMethod( "clear", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null );
		var addMenu = nativeMenuBarType.GetMethods( BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic )
			.FirstOrDefault( x =>
			{
				if ( x.Name != "addMenu" )
					return false;

				var parameters = x.GetParameters();
				return parameters.Length == 1 && parameters[0].ParameterType.FullName == "Native.QMenu";
			} );

		var menuField = typeof( Menu ).GetField( "_menu", BindingFlags.Instance | BindingFlags.NonPublic );
		if ( clear is null || addMenu is null || menuField is null )
			return;

		clear.Invoke( nativeMenuBar, null );

		foreach ( var menu in menus )
		{
			if ( menu is null || !menu.IsValid )
				continue;

			var nativeMenu = menuField.GetValue( menu );
			if ( nativeMenu is not null )
			{
				addMenu.Invoke( nativeMenuBar, new[] { nativeMenu } );
			}
		}
	}

	static void ApplyAllMenusFromObjectGraph( object root )
	{
		var seenObjects = new HashSet<object>( ReferenceEqualityComparer.Instance );
		var seenMenus = new HashSet<Menu>( ReferenceEqualityComparer.Instance );
		ApplyAllMenusFromObjectGraph( root, seenObjects, seenMenus, 0 );
	}

	static void ApplyAllMenusFromObjectGraph( object target, HashSet<object> seenObjects, HashSet<Menu> seenMenus, int depth )
	{
		if ( target is null || depth > 4 || !seenObjects.Add( target ) )
			return;

		if ( target is Menu menu && seenMenus.Add( menu ) )
		{
			ApplyMenu( menu );
		}
		else if ( target is MenuBar menuBar )
		{
			ApplyMenuBar( menuBar );
		}

		for ( var type = target.GetType(); type is not null; type = type.BaseType )
		{
			foreach ( var field in type.GetFields( BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic ) )
			{
				if ( field.FieldType == typeof( string ) || field.FieldType.IsValueType )
					continue;

				var value = field.GetValue( target );

				if ( value is Menu fieldMenu && seenMenus.Add( fieldMenu ) )
				{
					ApplyMenu( fieldMenu );
				}
				else if ( value is MenuBar fieldMenuBar )
				{
					ApplyMenuBar( fieldMenuBar );
				}
				else if ( value is System.Collections.IEnumerable enumerable && value is not string )
				{
					foreach ( var item in enumerable )
					{
						if ( item is Menu itemMenu && seenMenus.Add( itemMenu ) )
						{
							ApplyMenu( itemMenu );
						}
						else if ( item is MenuBar itemMenuBar )
						{
							ApplyMenuBar( itemMenuBar );
						}
					}
				}
			}
		}
	}

	static IEnumerable<string> DumpWidgetTree( Widget root )
	{
		var seen = new HashSet<Widget>( ReferenceEqualityComparer.Instance );
		return DumpWidgetTree( root, seen, 0 );
	}

	static IEnumerable<string> DumpWidgetTree( Widget widget, HashSet<Widget> seen, int depth )
	{
		if ( widget is null || !widget.IsValid || !seen.Add( widget ) )
			yield break;

		var line = DescribeWidget( widget );
		if ( !string.IsNullOrWhiteSpace( line ) )
			yield return $"{new string( ' ', depth * 2 )}{line}";

		foreach ( var child in widget.Children )
		{
			foreach ( var childLine in DumpWidgetTree( child, seen, depth + 1 ) )
			{
				yield return childLine;
			}
		}
	}

	static string DescribeWidget( Widget widget )
	{
		if ( widget is null || !widget.IsValid )
			return null;

		var parts = new List<string>
		{
			widget.GetType().FullName
		};

		if ( !string.IsNullOrWhiteSpace( widget.Name ) )
			parts.Add( $"Name=\"{widget.Name}\"" );

		foreach ( var value in GetDebugTextProperties( widget ) )
		{
			parts.Add( value );
		}

		return string.Join( " | ", parts );
	}

	static IEnumerable<string> GetDebugTextProperties( object target )
	{
		var seenNames = new HashSet<string>( StringComparer.Ordinal );
		var candidates = new[] { "Text", "Title", "WindowTitle", "ToolTip", "StatusTip", "PlaceholderText", "Description", "RootTitle", "Category" };

		for ( var type = target.GetType(); type is not null && type != typeof( object ); type = type.BaseType )
		{
			foreach ( var name in candidates )
			{
				if ( !seenNames.Add( $"{type.FullName}.{name}" ) )
					continue;

				var property = type.GetProperty( name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic );
				if ( property is null || property.PropertyType != typeof( string ) || property.GetIndexParameters().Length != 0 )
					continue;

				string value = null;
				try
				{
					value = property.GetValue( target ) as string;
				}
				catch
				{
				}

				if ( string.IsNullOrWhiteSpace( value ) )
					continue;

				yield return $"{name}=\"{value.Replace( "\r", "\\r" ).Replace( "\n", "\\n" )}\"";
			}
		}
	}
}

