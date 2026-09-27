using System.Reflection;
using EntityFrameworkCore.Encrypted.Annotations;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using EntityFrameworkCore.Encrypted.Common.Keys;
using EntityFrameworkCore.Encrypted.Providers;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace EntityFrameworkCore.Encrypted.Common.Plugin;

internal sealed class EncryptionConvention(DataKeyRing? keyRing, Type contextType) : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        // keys are resolved on first encrypt/decrypt, so building the model (migrations, design time) needs no keys
        var encryptionProvider = new Aes256EncryptionProvider(GetKey);
        var converter = new EncryptionConverter(encryptionProvider);

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

    private byte[] GetKey()
        => keyRing?.GetKey(contextType) ?? throw new EntityFrameworkEncryptionException(
            $"Encryption is not configured for {contextType.Name}. " +
            "Register it with services.AddEncryption(...) and create the context through dependency injection");
}
