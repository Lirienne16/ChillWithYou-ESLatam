using System.Security.Cryptography;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace ChillWithYouSpanishInstaller;

internal enum AssemblyPatchState
{
    Compatible,
    PatchedCurrent,
    PatchedOlder,
    LegacyPatched,
    Incompatible,
}

internal sealed record AssemblyInspection(
    AssemblyPatchState State,
    string Message,
    string? InstalledVersion = null);

internal static class SpanishPatchEngine
{
    private const string OverlayFullName = "SusumiMod.SpanishOverlay";
    private const string CreditText = "Español latinoamerica (MOD): Susumi";

    public static AssemblyInspection Inspect(string assemblyPath, string currentModVersion)
    {
        try
        {
            using var assembly = ReadAssembly(assemblyPath, out _);
            var types = Flatten(assembly.MainModule.Types).ToList();
            var overlay = types.FirstOrDefault(type => type.FullName == OverlayFullName);
            if (overlay is not null)
            {
                var version = overlay.Fields.FirstOrDefault(field => field.Name == "Version")?.Constant as string;
                return new(
                    version == currentModVersion ? AssemblyPatchState.PatchedCurrent : AssemblyPatchState.PatchedOlder,
                    version == currentModVersion
                        ? "La capa de traducción está instalada."
                        : "Hay una versión anterior de la capa de traducción instalada.",
                    version);
            }

            var portugueseLabels = CountStringLiterals(types, "Português");
            var spanishLabels = CountStringLiterals(types, "Español (Latinoamérica)");
            var portugueseCultures = CountStringLiterals(types, "pt-BR");
            var spanishCultures = CountStringLiterals(types, "es-MX");
            var credit = CountStringLiterals(types, CreditText);
            if (spanishLabels > 0 || spanishCultures > 0 || credit > 0)
                return new(AssemblyPatchState.LegacyPatched,
                    "Se detectó una instalación de la arquitectura anterior del mod.");

            ValidateStructure(types, portugueseLabels, portugueseCultures);
            return new(AssemblyPatchState.Compatible,
                "La versión del juego es compatible con el parche dinámico.");
        }
        catch (Exception ex)
        {
            return new(AssemblyPatchState.Incompatible,
                $"La estructura del juego no es compatible: {ex.Message}");
        }
    }

    public static string Patch(
        string inputPath,
        string outputPath,
        IReadOnlyDictionary<string, string> translations,
        string modVersion)
    {
        if (translations.Count == 0)
            throw new InvalidDataException("La traducción integrada está vacía.");

        using var assembly = ReadAssembly(inputPath, out var resolver);
        var module = assembly.MainModule;
        var types = Flatten(module.Types).ToList();
        if (types.Any(type => type.FullName == OverlayFullName))
            throw new InvalidOperationException("La DLL ya contiene la capa de traducción.");

        int portugueseLabels = CountStringLiterals(types, "Português");
        int cultures = CountStringLiterals(types, "pt-BR");
        ValidateStructure(types, portugueseLabels, cultures);

        var overlayTryGet = AddTranslationOverlay(module, translations, modVersion);
        PatchLocalizationLookup(module, types, overlayTryGet);

        var languageSupplier = types.Single(type => type.FullName == "Bulbul.LanguageSupplier");
        ForcePortugueseInGet(languageSupplier.Methods.Single(method => method.Name == "Get" && method.Parameters.Count == 0));
        ForcePortugueseInSet(languageSupplier.Methods.Single(method => method.Name == "Set" && method.Parameters.Count == 1));
        AddSpanishCredit(module, resolver, types.Single(type => type.FullName == "Bulbul.SettingUI"));

        portugueseLabels = ReplaceStringLiterals(types, "Português", "Español (Latinoamérica)");
        cultures = ReplaceStringLiterals(types, "pt-BR", "es-MX");
        int newsFields = RedirectFields(
            types.Single(type => type.FullName == "Bulbul.News"),
            new Dictionary<string, string>
            {
                ["Title_br"] = "Title_en",
                ["MainText_br"] = "MainText_en",
            });
        int tutorialFields = RedirectFields(
            types.Single(type => type.FullName == "TutorialView"),
            new Dictionary<string, string>
            {
                ["_screenSpritePR"] = "_screenSpriteEN",
                ["_pomodoroTimerSpritePR"] = "_pomodoroTimerSpriteEN",
                ["_levelAndStorySpritePR"] = "_levelAndStorySpriteEN",
                ["_newEnvironmentSpritePR"] = "_newEnvironmentSpriteEN",
            });

        if (portugueseLabels < 1 || cultures < 1 || newsFields != 2 || tutorialFields != 4)
            throw new InvalidOperationException(
                $"La estructura cambió durante el parcheo: etiquetas={portugueseLabels}, " +
                $"culturas={cultures}, noticias={newsFields}, tutoriales={tutorialFields}.");

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        assembly.Write(outputPath, new WriterParameters { WriteSymbols = false });

        var verification = Inspect(outputPath, modVersion);
        if (verification.State != AssemblyPatchState.PatchedCurrent)
            throw new InvalidDataException($"La DLL parcheada no superó la verificación: {verification.Message}");

        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(outputPath))).ToLowerInvariant();
    }

    private static AssemblyDefinition ReadAssembly(string path, out DefaultAssemblyResolver resolver)
    {
        resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        return AssemblyDefinition.ReadAssembly(path, new ReaderParameters
        {
            AssemblyResolver = resolver,
            ReadSymbols = false,
            InMemory = true,
        });
    }

    private static void ValidateStructure(IReadOnlyList<TypeDefinition> types, int portugueseLabels, int cultures)
    {
        if (portugueseLabels < 1 || cultures < 1)
            throw new InvalidOperationException("No se encontraron las etiquetas base del idioma portugués.");

        var languageSupplier = types.Single(type => type.FullName == "Bulbul.LanguageSupplier");
        _ = languageSupplier.Methods.Single(method => method.Name == "Get" && method.Parameters.Count == 0);
        _ = languageSupplier.Methods.Single(method => method.Name == "Set" && method.Parameters.Count == 1);

        var wrapper = types.Single(type => type.FullName == "Bulbul.LocalizationMasterWrapper");
        _ = wrapper.Methods.Single(method => method.Name == "Get" && method.Parameters.Count == 2 &&
            method.Parameters[0].ParameterType.FullName == "Bulbul.MasterData.LocalizationData" &&
            method.Parameters[1].ParameterType.FullName == "Bulbul.GameLanguageType");
        _ = types.Single(type => type.FullName == "Bulbul.MasterData.LocalizationData")
            .Fields.Single(field => field.Name == "ID");

        var creditMethod = types.Single(type => type.FullName == "Bulbul.SettingUI")
            .Methods.Single(method => method.Name == "OnOpenCreditTab" && method.Parameters.Count == 0);
        if (!creditMethod.HasBody || creditMethod.Body.Instructions.Count != 1 ||
            creditMethod.Body.Instructions[0].OpCode != OpCodes.Ret)
            throw new InvalidOperationException("La sección de créditos cambió.");

        var news = types.Single(type => type.FullName == "Bulbul.News");
        foreach (var field in new[] { "Title_br", "Title_en", "MainText_br", "MainText_en" })
            _ = news.Fields.Single(candidate => candidate.Name == field);

        var tutorial = types.Single(type => type.FullName == "TutorialView");
        foreach (var field in new[]
        {
            "_screenSpritePR", "_screenSpriteEN", "_pomodoroTimerSpritePR", "_pomodoroTimerSpriteEN",
            "_levelAndStorySpritePR", "_levelAndStorySpriteEN", "_newEnvironmentSpritePR", "_newEnvironmentSpriteEN",
        })
            _ = tutorial.Fields.Single(candidate => candidate.Name == field);
    }

    private static MethodDefinition AddTranslationOverlay(
        ModuleDefinition module,
        IReadOnlyDictionary<string, string> translations,
        string modVersion)
    {
        var dictionaryAddTemplate = FindDictionaryMethodReference(module, "Add", 2);
        var dictionaryTryGetTemplate = FindDictionaryMethodReference(module, "TryGetValue", 2);
        var dictionaryElementType = ((GenericInstanceType)dictionaryAddTemplate.DeclaringType).ElementType;
        if (dictionaryElementType.Scope.Name != "netstandard")
            throw new InvalidOperationException(
                $"Dictionary<TKey,TValue> usa una biblioteca inesperada: {dictionaryElementType.Scope.Name}.");

        var dictionaryType = new GenericInstanceType(module.ImportReference(dictionaryElementType));
        dictionaryType.GenericArguments.Add(module.TypeSystem.String);
        dictionaryType.GenericArguments.Add(module.TypeSystem.String);

        var overlay = new TypeDefinition(
            "SusumiMod",
            "SpanishOverlay",
            Mono.Cecil.TypeAttributes.NotPublic | Mono.Cecil.TypeAttributes.Abstract |
            Mono.Cecil.TypeAttributes.Sealed | Mono.Cecil.TypeAttributes.BeforeFieldInit,
            module.TypeSystem.Object);
        module.Types.Add(overlay);

        overlay.Fields.Add(new FieldDefinition(
            "Version",
            Mono.Cecil.FieldAttributes.Public | Mono.Cecil.FieldAttributes.Static |
            Mono.Cecil.FieldAttributes.Literal | Mono.Cecil.FieldAttributes.HasDefault,
            module.TypeSystem.String)
        {
            Constant = modVersion,
        });
        var translationsField = new FieldDefinition(
            "Translations",
            Mono.Cecil.FieldAttributes.Private | Mono.Cecil.FieldAttributes.Static |
            Mono.Cecil.FieldAttributes.InitOnly,
            dictionaryType);
        overlay.Fields.Add(translationsField);

        var dictionaryCtor = new MethodReference(".ctor", module.TypeSystem.Void, dictionaryType)
        {
            HasThis = true,
        };
        dictionaryCtor.Parameters.Add(new ParameterDefinition(module.TypeSystem.Int32));
        var dictionaryAdd = RetargetGenericInstanceMethod(dictionaryAddTemplate, dictionaryType);
        var dictionaryTryGet = RetargetGenericInstanceMethod(dictionaryTryGetTemplate, dictionaryType);

        var ordered = translations.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
        var chunkMethods = new List<MethodDefinition>();
        for (int offset = 0, chunkIndex = 0; offset < ordered.Length; offset += 160, chunkIndex++)
        {
            var chunk = new MethodDefinition(
                $"AddChunk{chunkIndex}",
                Mono.Cecil.MethodAttributes.Private | Mono.Cecil.MethodAttributes.Static |
                Mono.Cecil.MethodAttributes.HideBySig,
                module.TypeSystem.Void);
            overlay.Methods.Add(chunk);
            chunkMethods.Add(chunk);
            var il = chunk.Body.GetILProcessor();
            foreach (var pair in ordered.Skip(offset).Take(160))
            {
                il.Append(il.Create(OpCodes.Ldsfld, translationsField));
                il.Append(il.Create(OpCodes.Ldstr, pair.Key));
                il.Append(il.Create(OpCodes.Ldstr, pair.Value));
                il.Append(il.Create(OpCodes.Callvirt, dictionaryAdd));
            }
            il.Append(il.Create(OpCodes.Ret));
        }

        var initializer = new MethodDefinition(
            ".cctor",
            Mono.Cecil.MethodAttributes.Private | Mono.Cecil.MethodAttributes.Static |
            Mono.Cecil.MethodAttributes.HideBySig | Mono.Cecil.MethodAttributes.SpecialName |
            Mono.Cecil.MethodAttributes.RTSpecialName,
            module.TypeSystem.Void);
        overlay.Methods.Add(initializer);
        var initializerIl = initializer.Body.GetILProcessor();
        initializerIl.Append(initializerIl.Create(OpCodes.Ldc_I4, ordered.Length));
        initializerIl.Append(initializerIl.Create(OpCodes.Newobj, dictionaryCtor));
        initializerIl.Append(initializerIl.Create(OpCodes.Stsfld, translationsField));
        foreach (var chunk in chunkMethods)
            initializerIl.Append(initializerIl.Create(OpCodes.Call, chunk));
        initializerIl.Append(initializerIl.Create(OpCodes.Ret));

        var tryGet = new MethodDefinition(
            "TryGet",
            Mono.Cecil.MethodAttributes.Public | Mono.Cecil.MethodAttributes.Static |
            Mono.Cecil.MethodAttributes.HideBySig,
            module.TypeSystem.Boolean);
        tryGet.Parameters.Add(new ParameterDefinition("id", Mono.Cecil.ParameterAttributes.None, module.TypeSystem.String));
        tryGet.Parameters.Add(new ParameterDefinition(
            "translation",
            Mono.Cecil.ParameterAttributes.Out,
            new ByReferenceType(module.TypeSystem.String)));
        overlay.Methods.Add(tryGet);
        var tryGetIl = tryGet.Body.GetILProcessor();
        tryGetIl.Append(tryGetIl.Create(OpCodes.Ldsfld, translationsField));
        tryGetIl.Append(tryGetIl.Create(OpCodes.Ldarg_0));
        tryGetIl.Append(tryGetIl.Create(OpCodes.Ldarg_1));
        tryGetIl.Append(tryGetIl.Create(OpCodes.Callvirt, dictionaryTryGet));
        tryGetIl.Append(tryGetIl.Create(OpCodes.Ret));
        return tryGet;
    }

    private static MethodReference FindDictionaryMethodReference(
        ModuleDefinition module,
        string methodName,
        int parameterCount)
    {
        return Flatten(module.Types)
            .SelectMany(type => type.Methods)
            .Where(method => method.HasBody)
            .SelectMany(method => method.Body.Instructions)
            .Select(instruction => instruction.Operand)
            .OfType<MethodReference>()
            .FirstOrDefault(candidate =>
                candidate.Name == methodName &&
                candidate.Parameters.Count == parameterCount &&
                candidate.DeclaringType is GenericInstanceType instance &&
                instance.ElementType.FullName == "System.Collections.Generic.Dictionary`2")
            ?? throw new InvalidOperationException(
                $"No se encontró una referencia compatible a Dictionary<TKey,TValue>.{methodName}.");
    }

    private static MethodReference RetargetGenericInstanceMethod(
        MethodReference template,
        GenericInstanceType declaringType)
    {
        var reference = new MethodReference(template.Name, template.ReturnType, declaringType)
        {
            HasThis = template.HasThis,
            ExplicitThis = template.ExplicitThis,
            CallingConvention = template.CallingConvention,
        };
        foreach (var parameter in template.Parameters)
            reference.Parameters.Add(new ParameterDefinition(
                parameter.Name,
                parameter.Attributes,
                parameter.ParameterType));
        foreach (var genericParameter in template.GenericParameters)
            reference.GenericParameters.Add(new GenericParameter(genericParameter.Name, reference));
        return reference;
    }

    private static void PatchLocalizationLookup(
        ModuleDefinition module,
        IReadOnlyList<TypeDefinition> types,
        MethodDefinition overlayTryGet)
    {
        var wrapper = types.Single(type => type.FullName == "Bulbul.LocalizationMasterWrapper");
        var method = wrapper.Methods.Single(candidate => candidate.Name == "Get" && candidate.Parameters.Count == 2 &&
            candidate.Parameters[0].ParameterType.FullName == "Bulbul.MasterData.LocalizationData" &&
            candidate.Parameters[1].ParameterType.FullName == "Bulbul.GameLanguageType");
        var idField = types.Single(type => type.FullName == "Bulbul.MasterData.LocalizationData")
            .Fields.Single(field => field.Name == "ID");

        method.Body.InitLocals = true;
        var translated = new VariableDefinition(module.TypeSystem.String);
        method.Body.Variables.Add(translated);
        var original = method.Body.Instructions[0];
        var il = method.Body.GetILProcessor();
        il.InsertBefore(original, il.Create(OpCodes.Ldarg, method.Parameters[1]));
        il.InsertBefore(original, il.Create(OpCodes.Ldc_I4_5));
        il.InsertBefore(original, il.Create(OpCodes.Bne_Un, original));
        il.InsertBefore(original, il.Create(OpCodes.Ldarg, method.Parameters[0]));
        il.InsertBefore(original, il.Create(OpCodes.Ldfld, idField));
        il.InsertBefore(original, il.Create(OpCodes.Ldloca, translated));
        il.InsertBefore(original, il.Create(OpCodes.Call, overlayTryGet));
        il.InsertBefore(original, il.Create(OpCodes.Brfalse, original));
        il.InsertBefore(original, il.Create(OpCodes.Ldloc, translated));
        il.InsertBefore(original, il.Create(OpCodes.Ret));
    }

    private static IEnumerable<TypeDefinition> Flatten(IEnumerable<TypeDefinition> roots)
    {
        foreach (var type in roots)
        {
            yield return type;
            foreach (var nested in Flatten(type.NestedTypes))
                yield return nested;
        }
    }

    private static int CountStringLiterals(IEnumerable<TypeDefinition> types, string value) => types
        .SelectMany(type => type.Methods)
        .Where(method => method.HasBody)
        .SelectMany(method => method.Body.Instructions)
        .Count(instruction => instruction.OpCode == OpCodes.Ldstr && instruction.Operand as string == value);

    private static void ForcePortugueseInGet(MethodDefinition method)
    {
        if (!method.HasBody || method.Body.Variables.Count == 0)
            throw new InvalidOperationException("LanguageSupplier.Get no tiene la estructura esperada.");

        var instructions = method.Body.Instructions;
        var store = instructions.FirstOrDefault(IsStoreLocal)
            ?? throw new InvalidOperationException("No se encontró la variable de idioma guardado en Get.");
        var local = GetStoredLocal(method.Body, store);
        var setter = instructions
            .Select(instruction => instruction.Operand as MethodReference)
            .FirstOrDefault(reference => reference?.Name == "set_Value" && reference.Parameters.Count == 1 &&
                reference.DeclaringType.FullName.StartsWith("R3.ReactiveProperty`1<", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("No se encontró ReactiveProperty<GameLanguageType>.set_Value.");

        var il = method.Body.GetILProcessor();
        var load = il.Create(OpCodes.Ldloc, local);
        var value = il.Create(OpCodes.Ldc_I4_5);
        var call = il.Create(OpCodes.Callvirt, setter);
        il.InsertAfter(store, load);
        il.InsertAfter(load, value);
        il.InsertAfter(value, call);
    }

    private static void ForcePortugueseInSet(MethodDefinition method)
    {
        if (!method.HasBody || method.Parameters.Count != 1)
            throw new InvalidOperationException("LanguageSupplier.Set no tiene la estructura esperada.");
        var il = method.Body.GetILProcessor();
        var first = method.Body.Instructions.First();
        il.InsertBefore(first, il.Create(OpCodes.Ldc_I4_5));
        il.InsertBefore(first, il.Create(OpCodes.Starg, method.Parameters[0]));
    }

    private static bool IsStoreLocal(Instruction instruction) => instruction.OpCode.Code is
        Code.Stloc or Code.Stloc_S or Code.Stloc_0 or Code.Stloc_1 or Code.Stloc_2 or Code.Stloc_3;

    private static VariableDefinition GetStoredLocal(MethodBody body, Instruction instruction) => instruction.OpCode.Code switch
    {
        Code.Stloc_0 => body.Variables[0],
        Code.Stloc_1 => body.Variables[1],
        Code.Stloc_2 => body.Variables[2],
        Code.Stloc_3 => body.Variables[3],
        Code.Stloc or Code.Stloc_S => (VariableDefinition)instruction.Operand,
        _ => throw new InvalidOperationException("Instrucción stloc no compatible."),
    };

    private static int ReplaceStringLiterals(IEnumerable<TypeDefinition> types, string oldValue, string newValue)
    {
        int count = 0;
        foreach (var instruction in types.SelectMany(type => type.Methods)
            .Where(method => method.HasBody).SelectMany(method => method.Body.Instructions))
        {
            if (instruction.OpCode == OpCodes.Ldstr && instruction.Operand as string == oldValue)
            {
                instruction.Operand = newValue;
                count++;
            }
        }
        return count;
    }

    private static int RedirectFields(TypeDefinition type, IReadOnlyDictionary<string, string> redirects)
    {
        var targets = redirects.ToDictionary(pair => pair.Key,
            pair => type.Fields.Single(field => field.Name == pair.Value));
        int count = 0;
        foreach (var instruction in type.Methods.Where(method => method.HasBody)
            .SelectMany(method => method.Body.Instructions))
        {
            if (instruction.Operand is FieldReference field && redirects.ContainsKey(field.Name))
            {
                instruction.Operand = targets[field.Name];
                count++;
            }
        }
        return count;
    }

    private static void AddSpanishCredit(ModuleDefinition module, DefaultAssemblyResolver resolver, TypeDefinition settingUi)
    {
        var method = settingUi.Methods.Single(candidate => candidate.Name == "OnOpenCreditTab" && candidate.Parameters.Count == 0);
        if (!method.HasBody || method.Body.Instructions.Count != 1 || method.Body.Instructions[0].OpCode != OpCodes.Ret)
            throw new InvalidOperationException("SettingUI.OnOpenCreditTab no tiene la estructura esperada.");

        AssemblyDefinition Resolve(string name)
        {
            var reference = module.AssemblyReferences.Single(candidate => candidate.Name == name);
            return resolver.Resolve(reference);
        }

        using var core = Resolve("UnityEngine.CoreModule");
        using var ui = Resolve("UnityEngine.UI");
        using var textMeshPro = Resolve("Unity.TextMeshPro");
        var transformType = core.MainModule.GetType("UnityEngine.Transform");
        var componentType = core.MainModule.GetType("UnityEngine.Component");
        var gameObjectType = core.MainModule.GetType("UnityEngine.GameObject");
        var objectType = core.MainModule.GetType("UnityEngine.Object");
        var rectTransformType = core.MainModule.GetType("UnityEngine.RectTransform");
        var scrollRectType = ui.MainModule.GetType("UnityEngine.UI.ScrollRect");
        var textType = textMeshPro.MainModule.GetType("TMPro.TextMeshProUGUI");
        var tmpTextType = textMeshPro.MainModule.GetType("TMPro.TMP_Text");

        var getContent = module.ImportReference(scrollRectType.Methods.Single(candidate => candidate.Name == "get_content" && candidate.Parameters.Count == 0));
        var find = module.ImportReference(transformType.Methods.Single(candidate => candidate.Name == "Find" && candidate.Parameters.Count == 1 && candidate.Parameters[0].ParameterType.FullName == "System.String"));
        var getComponentsDefinition = componentType.Methods.Single(candidate => candidate.Name == "GetComponentsInChildren" && candidate.HasGenericParameters && candidate.Parameters.Count == 1 && candidate.Parameters[0].ParameterType.FullName == "System.Boolean");
        var getComponents = new GenericInstanceMethod(module.ImportReference(getComponentsDefinition));
        getComponents.GenericArguments.Add(module.ImportReference(textType));
        var getGameObject = module.ImportReference(componentType.Methods.Single(candidate => candidate.Name == "get_gameObject" && candidate.Parameters.Count == 0));
        var instantiateDefinition = objectType.Methods.Single(candidate => candidate.Name == "Instantiate" && candidate.HasGenericParameters && candidate.Parameters.Count == 3 && candidate.Parameters[1].ParameterType.FullName == "UnityEngine.Transform" && candidate.Parameters[2].ParameterType.FullName == "System.Boolean");
        var instantiate = new GenericInstanceMethod(module.ImportReference(instantiateDefinition));
        instantiate.GenericArguments.Add(module.ImportReference(gameObjectType));
        var setName = module.ImportReference(objectType.Methods.Single(candidate => candidate.Name == "set_name" && candidate.Parameters.Count == 1));
        var getComponentDefinition = gameObjectType.Methods.Single(candidate => candidate.Name == "GetComponent" && candidate.HasGenericParameters && candidate.Parameters.Count == 0);
        var getComponent = new GenericInstanceMethod(module.ImportReference(getComponentDefinition));
        getComponent.GenericArguments.Add(module.ImportReference(textType));
        var setText = module.ImportReference(tmpTextType.Methods.Single(candidate => candidate.Name == "set_text" && candidate.Parameters.Count == 1));
        var setAsLastSibling = module.ImportReference(transformType.Methods.Single(candidate => candidate.Name == "SetAsLastSibling" && candidate.Parameters.Count == 0));
        var getTransform = module.ImportReference(gameObjectType.Methods.Single(candidate => candidate.Name == "get_transform" && candidate.Parameters.Count == 0));

        var creditScrollRect = settingUi.Fields.Single(field => field.Name == "_creditScrollRect");
        method.Body.InitLocals = true;
        var content = new VariableDefinition(module.ImportReference(rectTransformType));
        var texts = new VariableDefinition(new ArrayType(module.ImportReference(textType)));
        var clone = new VariableDefinition(module.ImportReference(gameObjectType));
        var creditText = new VariableDefinition(module.ImportReference(textType));
        method.Body.Variables.Add(content);
        method.Body.Variables.Add(texts);
        method.Body.Variables.Add(clone);
        method.Body.Variables.Add(creditText);

        var il = method.Body.GetILProcessor();
        var end = method.Body.Instructions[0];
        var hasTexts = il.Create(OpCodes.Nop);
        il.InsertBefore(end, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(end, il.Create(OpCodes.Ldfld, creditScrollRect));
        il.InsertBefore(end, il.Create(OpCodes.Callvirt, getContent));
        il.InsertBefore(end, il.Create(OpCodes.Stloc, content));
        il.InsertBefore(end, il.Create(OpCodes.Ldloc, content));
        il.InsertBefore(end, il.Create(OpCodes.Ldstr, "SpanishLatamModCredit"));
        il.InsertBefore(end, il.Create(OpCodes.Callvirt, find));
        il.InsertBefore(end, il.Create(OpCodes.Brtrue, end));
        il.InsertBefore(end, il.Create(OpCodes.Ldloc, content));
        il.InsertBefore(end, il.Create(OpCodes.Ldc_I4_1));
        il.InsertBefore(end, il.Create(OpCodes.Callvirt, getComponents));
        il.InsertBefore(end, il.Create(OpCodes.Stloc, texts));
        il.InsertBefore(end, il.Create(OpCodes.Ldloc, texts));
        il.InsertBefore(end, il.Create(OpCodes.Ldlen));
        il.InsertBefore(end, il.Create(OpCodes.Conv_I4));
        il.InsertBefore(end, il.Create(OpCodes.Ldc_I4_0));
        il.InsertBefore(end, il.Create(OpCodes.Bgt, hasTexts));
        il.InsertBefore(end, il.Create(OpCodes.Br, end));
        il.InsertBefore(end, hasTexts);
        il.InsertBefore(end, il.Create(OpCodes.Ldloc, texts));
        il.InsertBefore(end, il.Create(OpCodes.Ldloc, texts));
        il.InsertBefore(end, il.Create(OpCodes.Ldlen));
        il.InsertBefore(end, il.Create(OpCodes.Conv_I4));
        il.InsertBefore(end, il.Create(OpCodes.Ldc_I4_1));
        il.InsertBefore(end, il.Create(OpCodes.Sub));
        il.InsertBefore(end, il.Create(OpCodes.Ldelem_Ref));
        il.InsertBefore(end, il.Create(OpCodes.Callvirt, getGameObject));
        il.InsertBefore(end, il.Create(OpCodes.Ldloc, content));
        il.InsertBefore(end, il.Create(OpCodes.Ldc_I4_0));
        il.InsertBefore(end, il.Create(OpCodes.Call, instantiate));
        il.InsertBefore(end, il.Create(OpCodes.Stloc, clone));
        il.InsertBefore(end, il.Create(OpCodes.Ldloc, clone));
        il.InsertBefore(end, il.Create(OpCodes.Ldstr, "SpanishLatamModCredit"));
        il.InsertBefore(end, il.Create(OpCodes.Callvirt, setName));
        il.InsertBefore(end, il.Create(OpCodes.Ldloc, clone));
        il.InsertBefore(end, il.Create(OpCodes.Callvirt, getComponent));
        il.InsertBefore(end, il.Create(OpCodes.Stloc, creditText));
        il.InsertBefore(end, il.Create(OpCodes.Ldloc, creditText));
        il.InsertBefore(end, il.Create(OpCodes.Ldstr, CreditText));
        il.InsertBefore(end, il.Create(OpCodes.Callvirt, setText));
        il.InsertBefore(end, il.Create(OpCodes.Ldloc, clone));
        il.InsertBefore(end, il.Create(OpCodes.Callvirt, getTransform));
        il.InsertBefore(end, il.Create(OpCodes.Callvirt, setAsLastSibling));
    }
}
