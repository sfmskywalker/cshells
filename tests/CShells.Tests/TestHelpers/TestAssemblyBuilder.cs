using System.Reflection;
using System.Reflection.Emit;
using CShells.Features;
using Microsoft.Extensions.DependencyInjection;

namespace CShells.Tests.TestHelpers;

/// <summary>
/// Helper class for creating dynamic test assemblies with shell feature types.
/// </summary>
public static class TestAssemblyBuilder
{
    /// <summary>
    /// Creates a dynamic assembly with test feature types for testing purposes.
    /// </summary>
    public static Assembly CreateTestAssembly(params (string FeatureName, Type? ImplementInterface, object[] Dependencies, object[] Metadata)[] featureDefinitions)
    {
        var assemblyName = new AssemblyName($"TestAssembly_{Guid.NewGuid():N}");
        var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
        var moduleBuilder = assemblyBuilder.DefineDynamicModule("MainModule");

        var uniqueCounter = 0;
        foreach (var (featureName, implementInterface, dependencies, metadata) in featureDefinitions)
        {
            var typeName = $"{featureName}_{uniqueCounter++}";

            // Create the type, optionally implementing IShellFeature
            TypeBuilder typeBuilder;
            if (implementInterface == typeof(IShellFeature))
            {
                typeBuilder = moduleBuilder.DefineType(typeName, TypeAttributes.Public | TypeAttributes.Class);
                typeBuilder.AddInterfaceImplementation(typeof(IShellFeature));

                // Implement ConfigureServices method with signature (IServiceCollection)
                var configureServicesMethod = typeBuilder.DefineMethod(
                    "ConfigureServices",
                    MethodAttributes.Public | MethodAttributes.Virtual,
                    typeof(void),
                    [typeof(IServiceCollection)]);
                var ilConfigureServices = configureServicesMethod.GetILGenerator();
                ilConfigureServices.Emit(OpCodes.Ret);
            }
            else
            {
                typeBuilder = moduleBuilder.DefineType(typeName, TypeAttributes.Public | TypeAttributes.Class);
            }

            // Add the ShellFeatureAttribute
            var attributeConstructor = typeof(ShellFeatureAttribute).GetConstructor([typeof(string)])!;
            var attributeBuilder = new CustomAttributeBuilder(
                attributeConstructor,
                [featureName],
                [
                    typeof(ShellFeatureAttribute).GetProperty("DependsOn")!,
                    typeof(ShellFeatureAttribute).GetProperty("Metadata")!
                ],
                [dependencies, metadata]);
            typeBuilder.SetCustomAttribute(attributeBuilder);

            typeBuilder.CreateType();
        }

        return assemblyBuilder;
    }

    /// <summary>
    /// Creates a dynamic assembly with feature types and service registrations.
    /// </summary>
    public static Assembly CreateTestAssemblyWithServices(params (string FeatureName, Type StartupType, object[] Dependencies)[] featureDefinitions)
    {
        var assemblyName = new AssemblyName($"TestAssembly_{Guid.NewGuid():N}");
        var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
        var moduleBuilder = assemblyBuilder.DefineDynamicModule("MainModule");

        var uniqueCounter = 0;
        foreach (var (featureName, startupType, dependencies) in featureDefinitions)
        {
            var typeName = $"{featureName}Startup_{uniqueCounter++}";
            var typeBuilder = moduleBuilder.DefineType(typeName, TypeAttributes.Public | TypeAttributes.Class);
            typeBuilder.AddInterfaceImplementation(typeof(IShellFeature));

            // Implement ConfigureServices method with signature (IServiceCollection)
            var configureServicesMethod = typeBuilder.DefineMethod(
                "ConfigureServices",
                MethodAttributes.Public | MethodAttributes.Virtual,
                typeof(void),
                [typeof(IServiceCollection)]);
            var ilConfigureServices = configureServicesMethod.GetILGenerator();
            ilConfigureServices.Emit(OpCodes.Ret);

            // Add the ShellFeatureAttribute
            var attributeConstructor = typeof(ShellFeatureAttribute).GetConstructor([typeof(string)])!;
            var attributeBuilder = new CustomAttributeBuilder(
                attributeConstructor,
                [featureName],
                [typeof(ShellFeatureAttribute).GetProperty("DependsOn")!],
                [dependencies]);
            typeBuilder.SetCustomAttribute(attributeBuilder);

            typeBuilder.CreateType();
        }

        return assemblyBuilder;
    }

    /// <summary>
    /// Creates a feature type in a module with service registration.
    /// </summary>
    public static void CreateFeatureType(ModuleBuilder moduleBuilder, string featureName, object[] dependencies, Type serviceInterface, Type serviceImplementation)
    {
        var typeName = $"{featureName}Startup";
        var typeBuilder = moduleBuilder.DefineType(typeName, TypeAttributes.Public | TypeAttributes.Class);
        typeBuilder.AddInterfaceImplementation(typeof(IShellFeature));

        // Implement ConfigureServices method that registers a service
        DefineConfigureServicesWithService(typeBuilder, serviceInterface, serviceImplementation);

        // Add the ShellFeatureAttribute
        var attributeConstructor = typeof(ShellFeatureAttribute).GetConstructor([typeof(string)])!;
        var attributeBuilder = new CustomAttributeBuilder(
            attributeConstructor,
            [featureName],
            [typeof(ShellFeatureAttribute).GetProperty("DependsOn")!],
            [dependencies]);
        typeBuilder.SetCustomAttribute(attributeBuilder);

        typeBuilder.CreateType();
    }

    /// <summary>
    /// Creates a dynamic assembly with feature types that do not have ShellFeatureAttribute.
    /// Used for testing feature name derivation from class names.
    /// </summary>
    public static Assembly CreateTestAssemblyWithoutAttribute(params string[] classNames)
    {
        var assemblyName = new AssemblyName($"TestAssembly_{Guid.NewGuid():N}");
        var assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
        var moduleBuilder = assemblyBuilder.DefineDynamicModule("MainModule");

        foreach (var className in classNames)
        {
            var typeBuilder = moduleBuilder.DefineType(className, TypeAttributes.Public | TypeAttributes.Class);
            typeBuilder.AddInterfaceImplementation(typeof(IShellFeature));

            // Implement ConfigureServices method with signature (IServiceCollection)
            var configureServicesMethod = typeBuilder.DefineMethod(
                "ConfigureServices",
                MethodAttributes.Public | MethodAttributes.Virtual,
                typeof(void),
                [typeof(IServiceCollection)]);
            var ilConfigureServices = configureServicesMethod.GetILGenerator();
            ilConfigureServices.Emit(OpCodes.Ret);

            typeBuilder.CreateType();
        }

        return assemblyBuilder;
    }

    /// <summary>
    /// Defines a ConfigureServices method that registers a service as a singleton.
    /// </summary>
    public static void DefineConfigureServicesWithService(TypeBuilder typeBuilder, Type serviceInterface, Type serviceImplementation)
    {
        // ConfigureServices takes (IServiceCollection services)
        var configureServicesMethod = typeBuilder.DefineMethod(
            "ConfigureServices",
            MethodAttributes.Public | MethodAttributes.Virtual,
            typeof(void),
            [typeof(IServiceCollection)]);

        var il = configureServicesMethod.GetILGenerator();

        // Load the services collection (first argument after 'this')
        il.Emit(OpCodes.Ldarg_1);

        // Call ServiceCollectionServiceExtensions.AddSingleton<TService, TImplementation>(services)
        var addSingletonMethod = typeof(ServiceCollectionServiceExtensions)
            .GetMethods()
            .First(m => m.Name == "AddSingleton" &&
                        m.IsGenericMethod &&
                        m.GetGenericArguments().Length == 2 &&
                        m.GetParameters().Length == 1)
            .MakeGenericMethod(serviceInterface, serviceImplementation);

        il.Emit(OpCodes.Call, addSingletonMethod);
        il.Emit(OpCodes.Pop); // Pop the return value (IServiceCollection)
        il.Emit(OpCodes.Ret);
    }
}
