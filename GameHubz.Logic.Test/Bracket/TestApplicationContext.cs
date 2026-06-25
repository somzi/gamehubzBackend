using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;

namespace GameHubz.Logic.Test.Bracket
{
    /// <summary>
    /// SQLite-friendly subclass of the production <see cref="ApplicationContext"/>. The only thing it
    /// changes is mapping <see cref="TournamentEntity.Countries"/> — a Postgres <c>text[]</c> the SQLite
    /// provider can't map — to a delimited string. Everything else (relationships, query filters, the
    /// UTC converters) comes from the real model via <c>base.OnModelCreating</c>, so tests still exercise
    /// the production schema shape.
    /// </summary>
    internal sealed class TestApplicationContext : ApplicationContext
    {
        // '|' is safe: ISO 3166 alpha-2 country codes are uppercase letters only.
        private const char CountriesSeparator = '|';

        public TestApplicationContext(DbContextOptions<TestApplicationContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            var converter = new ValueConverter<List<string>?, string?>(
                v => v == null ? null : string.Join(CountriesSeparator, v),
                v => string.IsNullOrEmpty(v)
                    ? null
                    : v.Split(CountriesSeparator, StringSplitOptions.None).ToList());

            var comparer = new ValueComparer<List<string>?>(
                (a, b) => (a == null && b == null) || (a != null && b != null && a.SequenceEqual(b)),
                v => v == null ? 0 : v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode())),
                v => v == null ? null : v.ToList());

            modelBuilder.Entity<TournamentEntity>()
                .Property(t => t.Countries)
                .HasConversion(converter, comparer);
        }
    }
}
