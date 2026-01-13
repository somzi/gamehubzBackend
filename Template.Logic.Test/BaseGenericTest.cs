using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using Template.Common;
using Template.Common.Interfaces;
using Template.Logic.Interfaces;
using Template.Logic.Test.Factories;
using Template.Logic.Test.Interfaces;
using Template.Logic.Test.Services;
using Template.Logic.TestInterfaces;

namespace Template.Logic.Test
{
    public abstract class BaseGenericTest<TFactory, TService, TDto, TDtoPost, TDtoEdit, TEntity>
          where TFactory : IServiceFactory<TService>
          where TService : ITestableGenericService<TDto, TDtoPost, TDtoEdit>, ITestableService
          where TEntity : BaseEntity
    {
        protected readonly IUserContextReader userContextReader;
        protected readonly List<TEntity> entities;

        public BaseGenericTest()
        {
            userContextReader = new UserContextReaderFactory().CreateService();
            entities = CreateObjects().ToList();
        }

        [SetUp]
        protected void Setup()
        {
            UnitOfWorkFactoryService.Init();
        }

        protected virtual bool Skip_Save_Unsuccessful => false;

        protected abstract IEnumerable<TEntity> CreateObjects();

        protected abstract void PopulateData(IAppUnitOfWork putDomUnitOfWork);

        protected abstract IEnumerable<TDtoPost> Config_Save_Successful();

        protected abstract IEnumerable<TDtoPost> Config_Save_Unsuccessful();

        protected virtual IEnumerable<string> Config_Delete_Unsuccessful()
        {
            yield return Guid.NewGuid().ToString();
        }

        protected virtual IEnumerable<string> Config_Delete_Successful()
        {
            return entities.Select(x => x.Id!.Value.ToString());
        }

        protected virtual IEnumerable<Action<IAppUnitOfWork>> Config_Save_Successful_PopulateData()
        {
            return [];
        }

        [Test]
        public async Task Save_Successful()
        {
            foreach (var entity in Config_Save_Successful())
            {
                Setup();
                await GenericTests.Save_Successful<
                    TFactory,
                    TService,
                    TDto,
                    TDtoPost,
                    TDtoEdit>
                (Config_Save_Successful_PopulateData().ToList(),
                    entity
                );
            }
        }

        [Test]
        public void Save_Unsuccessful()
        {
            if (Skip_Save_Unsuccessful)
            {
                ClassicAssert.IsTrue(true);
                return;
            }
            foreach (var entity in Config_Save_Unsuccessful())
            {
                Setup();
                GenericTests.Save_ValidationException
                    <TFactory,
                    TService,
                    TDto,
                    TDtoPost,
                    TDtoEdit>
                (
                [
                    this.PopulateData
                ],
                entity);
            }
        }

        [Test]
        public async Task Delete_Successful()
        {
            foreach (var entityId in Config_Delete_Successful())
            {
                Setup();
                await GenericTests.Delete_Successful<
                TFactory,
                TService,
                TDto,
                TDtoPost,
                TDtoEdit>(
                [
                    this.PopulateData
                ],
                entityId);
            }
        }

        [Test]
        public void Delete_Unsuccessful()
        {
            foreach (var entityId in Config_Delete_Unsuccessful())
            {
                Setup();
                GenericTests.Delete_Unsuccessful<
                  TFactory,
                  TService,
                  TDto,
                  TDtoPost,
                  TDtoEdit>(
                  [
                            this.PopulateData
                  ],
                  entityId);
            }
        }
    }
}