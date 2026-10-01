using System.Runtime.CompilerServices;
using EntityFrameworkCore.Encrypted.Common.Exceptions;
using EntityFrameworkCore.Encrypted.Common.Keys;
using EntityFrameworkCore.Encrypted.Common.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EntityFrameworkCore.Encrypted.Common.Plugin;

internal static class EncryptedModel
{
    private static readonly ConditionalWeakTable<IModel, ModelKeyRing> KeyRings = new();

    /// <summary>
    /// A model passed with <c>UseModel</c> (e.g. a compiled model) isn't built with the encryption conventions: its
    /// encrypted properties would be read and written in plaintext. Every model built with them has the key table.
    /// Its converters use the keys of the application that built it: with <c>ReplaceService&lt;IModelCacheKeyFactory&gt;</c>,
    /// EF can reuse it for another application in the same process.
    /// </summary>
    public static void EnsureBuiltWithEncryption(DbContext context)
    {
        if (context.Model.FindEntityType(typeof(EncryptionKeyEntity)) == null)
            throw new EntityFrameworkEncryptionException(
                $"The model of {context.GetType().Name} wasn't built with encryption: UseEncryption() can't be combined with " +
                "UseModel(...), e.g. a compiled model, because encrypted properties are configured when the model is built");

        var modelKeyRing = KeyRings.GetValue(context.Model, FindKeyRing);
        var keyRing = context.GetService<IDbContextOptions>().FindExtension<EncryptionDbContextOptionsExtension>()?.KeyRing;

        if (modelKeyRing.HasEncryptedProperties && modelKeyRing.KeyRing != keyRing)
            throw new EntityFrameworkEncryptionException(
                $"The model of {context.GetType().Name} was built by another application in this process and uses its keys. " +
                "This happens when ReplaceService<IModelCacheKeyFactory, ...>() is combined with several service providers, " +
                "e.g. one per test: create each application with its own options, or don't replace the model cache key factory");
    }

    private static ModelKeyRing FindKeyRing(IModel model)
    {
        var encryptor = model.GetEntityTypes()
            .SelectMany(x => x.GetFlattenedProperties())
            .Select(x => (x.GetValueConverter() as IEncryptionConverter)?.Encryptor)
            .FirstOrDefault(x => x != null);

        return new ModelKeyRing(encryptor != null, encryptor?.KeyRing);
    }

    private sealed record ModelKeyRing(bool HasEncryptedProperties, DataKeyRing? KeyRing);
}
