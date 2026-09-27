using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace EntityFrameworkCore.Encrypted.Common.Plugin;

internal sealed class EncryptionConventionPlugin(IDbContextOptions options, ICurrentDbContext currentContext) : IConventionSetPlugin
{
    public ConventionSet ModifyConventions(ConventionSet conventionSet)
    {
        var extension = options.FindExtension<EncryptionDbContextOptionsExtension>();

        if (extension == null)
            return conventionSet;

        conventionSet.ModelFinalizingConventions.Add(
            new EncryptionConvention(extension.KeyRing, currentContext.Context.GetType()));

        return conventionSet;
    }
}
