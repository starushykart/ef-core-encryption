using System.Reflection;
using EntityFrameworkCore.Encryption.Common.Abstractions;
using EntityFrameworkCore.Encryption.Common.Exceptions;
using EntityFrameworkCore.Encryption.ModelBuilderExtensions;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace EntityFrameworkCore.Encryption.OptionsExtension.Plugin;

public class EncryptionConvention(IEncryptionProvider provider) : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        var converter = new EncryptionConverter(provider);
        
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            var encryptedProperties = entityType.GetProperties()
                .Where(x =>
                {
                    var annotation = x.FindAnnotation(PropertyAnnotations.IsEncrypted);

                    if (annotation?.Value != null && (bool)annotation.Value)
                        return true;

                    return x.PropertyInfo?.GetCustomAttribute<EncryptedAttribute>(false) != null;
                });

            foreach (var property in encryptedProperties)
            {
                if (property.ClrType != typeof(string))
                    throw new EntityFrameworkEncryptionException("Encryption could be applied only for string types");
               
                property.SetValueConverter(converter);
            }
        }
    }
}