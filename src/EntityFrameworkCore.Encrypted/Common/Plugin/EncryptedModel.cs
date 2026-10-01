using EntityFrameworkCore.Encrypted.Common.Exceptions;
using EntityFrameworkCore.Encrypted.Common.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EntityFrameworkCore.Encrypted.Common.Plugin;

internal static class EncryptedModel
{
    /// <summary>
    /// A model passed with <c>UseModel</c> (e.g. a compiled model) isn't built with the encryption conventions: its
    /// encrypted properties would be read and written in plaintext. Every model built with them has the key table.
    /// </summary>
    public static void EnsureBuiltWithEncryption(DbContext context)
    {
        if (context.Model.FindEntityType(typeof(EncryptionKeyEntity)) == null)
            throw new EntityFrameworkEncryptionException(
                $"The model of {context.GetType().Name} wasn't built with encryption: UseEncryption() can't be combined with " +
                "UseModel(...), e.g. a compiled model, because encrypted properties are configured when the model is built");
    }
}
