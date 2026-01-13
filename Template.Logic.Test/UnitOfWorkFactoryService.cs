using System;

using Microsoft.EntityFrameworkCore;
using Template.Data.Context;
using Template.Data.UnitOfWork;
using Template.Logic.Interfaces;
using Template.Logic.Test.Factories;
using Template.Logic.Utility;

namespace Template.Logic.Test
{
    public class UnitOfWorkFactoryService
    {
        public IUnitOfWorkFactory UnitOfWorkFactory { get; private set; }

        private static readonly Lazy<UnitOfWorkFactoryService> lazy =
                new(() => new UnitOfWorkFactoryService());

        public static UnitOfWorkFactoryService Instance
        {
            get
            {
                return lazy.Value;
            }
        }

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

        private UnitOfWorkFactoryService()
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        {
        }

        public static void Init()
        {
            Instance.UnitOfWorkFactory = CreateUnitOfWorkFactory();
        }

        private static IUnitOfWorkFactory CreateUnitOfWorkFactory()
        {
            DateTimeProvider? dateTimeProvider = new();
            SortStringBuilder? sortStringBuilder = new();
            FilterExpressionBuilder? filterExpressionBuilder = new();
            var localizationSerivce = new LocalizationServiceFactory().CreateService();

            var factory = new UnitOfWorkFactory(
                CreateInMemoryContext(),
                dateTimeProvider,
                filterExpressionBuilder,
                sortStringBuilder,
                localizationSerivce);

            return factory;
        }

        private static DbContextOptions<ApplicationContext> CreateInMemoryContext()
        {
            DbContextOptions<ApplicationContext>? options = new DbContextOptionsBuilder<ApplicationContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            return options;
        }
    }
}