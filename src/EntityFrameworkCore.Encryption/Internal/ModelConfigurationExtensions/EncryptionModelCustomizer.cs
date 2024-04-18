using System.Reflection;
using EntityFrameworkCore.Encryption.Common;
using EntityFrameworkCore.Encryption.Common.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace EntityFrameworkCore.Encryption.Internal.ModelConfigurationExtensions;

internal sealed class EncryptionModelCustomizer(ModelCustomizerDependencies dependencies, IEncryptionProvider provider) 
    : ModelCustomizer(dependencies)
{
    public override void Customize(ModelBuilder modelBuilder, DbContext context)
    {
        base.Customize(modelBuilder, context);
        
        var converter = new EncryptionConverter(provider);
        
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
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