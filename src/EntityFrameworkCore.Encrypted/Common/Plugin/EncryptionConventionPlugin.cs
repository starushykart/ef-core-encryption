using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Metadata.Conventions.Infrastructure;

namespace EntityFrameworkCore.Encrypted.Common.Plugin;

public class EncryptionConventionPlugin(IDbContextOptions options) : IConventionSetPlugin
{
    public ConventionSet ModifyConventions(ConventionSet conventionSet)
    {
        var extension = options.FindExtension<EncryptionDbContextOptionsExtension>();

        if (extension == null)
            return conventionSet;

        conventionSet.ModelFinalizingConventions.Add(new EncryptionConvention(extension.Provider));
        return conventionSet;
    }
}