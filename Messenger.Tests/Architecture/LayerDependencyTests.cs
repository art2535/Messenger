using Xunit;
using System.Reflection;
using Messenger.Core.Interfaces;
using Messenger.Infrastructure.Services.Administration;
using Messenger.Infrastructure.Services.Messages;

namespace Messenger.Tests.Architecture;

public sealed class LayerDependencyTests
{
    private static readonly string[] ForbiddenCoreDependencies =
    [
        "Messenger.API",
        "Messenger.Infrastructure",
        "Messenger.Web"
    ];

    [Fact]
    public void Core_DoesNotReferenceOuterApplicationLayers()
    {
        var references = typeof(IMessageService).Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var forbiddenDependency in ForbiddenCoreDependencies)
        {
            Assert.DoesNotContain(forbiddenDependency, references);
        }
    }

    [Fact]
    public void Web_DoesNotReferenceApiOrInfrastructure()
    {
        var webAssembly = Assembly.Load("Messenger.Web");
        var references = webAssembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("Messenger.API", references);
        Assert.DoesNotContain("Messenger.Infrastructure", references);
    }

    [Fact]
    public void Api_DoesNotReferenceWeb()
    {
        var apiAssembly = Assembly.Load("Messenger.API");
        var references = apiAssembly.GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("Messenger.Web", references);
    }

    [Fact]
    public void MessageService_DependsOnlyOnScenarioFacades()
    {
        var constructor = typeof(MessageService).GetConstructors().Single();
        var parameterTypes = constructor.GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.Equal(
            new[]
            {
                typeof(MessageQueryFacade),
                typeof(MessageCommandFacade),
                typeof(MessageReadFacade),
                typeof(MessageExportFacade)
            },
            parameterTypes);
    }

    [Fact]
    public void MessageScenarioFacades_AreSealedInfrastructureTypes()
    {
        var facadeTypes = new[]
        {
            typeof(MessageQueryFacade),
            typeof(MessageCommandFacade),
            typeof(MessageReadFacade),
            typeof(MessageExportFacade)
        };

        Assert.All(facadeTypes, facadeType =>
        {
            Assert.True(facadeType.IsSealed);
            Assert.Equal("Messenger.Infrastructure.Services.Messages", facadeType.Namespace);
        });
    }

    [Fact]
    public void AdminWorkflowService_UsesExplicitModerationDependency()
    {
        var constructor = typeof(AdminWorkflowService).GetConstructors().Single();
        var parameterTypes = constructor.GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.Contains(typeof(IModerationService), parameterTypes);
        Assert.DoesNotContain(typeof(IServiceProvider), parameterTypes);
    }

    [Fact]
    public void MessageEventPublisher_IsSealed()
    {
        Assert.True(typeof(MessageEventPublisher).IsSealed);
    }
}
