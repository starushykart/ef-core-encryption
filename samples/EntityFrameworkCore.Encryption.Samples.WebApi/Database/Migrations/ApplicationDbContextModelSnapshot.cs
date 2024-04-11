﻿// <auto-generated />
using System;
using EntityFrameworkCore.Encryption.Samples.WebApi.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace EntityFrameworkCore.Encryption.Samples.WebApi.Database.Migrations
{
    [DbContext(typeof(ApplicationDbContext))]
    partial class ApplicationDbContextModelSnapshot : ModelSnapshot
    {
        protected override void BuildModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasDefaultSchema("public")
                .HasAnnotation("ProductVersion", "8.0.3")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("EntityFrameworkCore.Encryption.Samples.WebApi.Models.Password", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<string>("EncryptedAttribute")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("EncryptedFluent")
                        .IsRequired()
                        .HasColumnType("text")
                        .HasAnnotation("Microsoft.EntityFrameworkCore.Encryption.IsEncrypted", true);

                    b.Property<string>("Original")
                        .IsRequired()
                        .HasColumnType("text");

                    b.HasKey("Id");

                    b.ToTable("Passwords", "public");
                });
#pragma warning restore 612, 618
        }
    }
}
