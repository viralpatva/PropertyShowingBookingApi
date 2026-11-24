using System.Reflection;
using ShowingBooking.Application.Slots;
using ShowingBooking.Domain;

namespace ShowingBooking.Tests.Unit;

/// <summary>Guards the dependency rule: inner layers must not know about outer ones.</summary>
public class ArchitectureTests
{
    private static IEnumerable<string> ReferencedAssemblyNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty);

    [Fact]
    public void Domain_depends_on_no_other_project_or_library()
    {
        var references = ReferencedAssemblyNames(typeof(Slot).Assembly).ToList();

        Assert.DoesNotContain(references, name => name.StartsWith("ShowingBooking."));
        Assert.DoesNotContain(references, name => name.StartsWith("Microsoft.EntityFrameworkCore"));
        Assert.DoesNotContain(references, name => name.StartsWith("MediatR"));
        Assert.DoesNotContain(references, name => name.StartsWith("FluentValidation"));
    }

    [Fact]
    public void Application_does_not_depend_on_EF_Core_or_outer_layers()
    {
        var references = ReferencedAssemblyNames(typeof(CreateSlotCommand).Assembly).ToList();

        Assert.DoesNotContain(references, name => name.StartsWith("Microsoft.EntityFrameworkCore"));
        Assert.DoesNotContain(references, name => name.StartsWith("Microsoft.Data.Sqlite"));
        Assert.DoesNotContain(references, name => name == "ShowingBooking.Infrastructure");
        Assert.DoesNotContain(references, name => name == "ShowingBooking.Api");
    }
}
