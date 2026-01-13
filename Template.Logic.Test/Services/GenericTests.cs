using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using NUnit.Framework;
using NUnit.Framework.Legacy;
using Template.Common;
using Template.Common.Extensions;
using Template.Common.Interfaces;
using Template.Common.Models;
using Template.DataModels.Models;
using Template.Logic.Exceptions;
using Template.Logic.Interfaces;
using Template.Logic.Test.Interfaces;
using Template.Logic.TestInterfaces;
using Template.Logic.Utility;

namespace Template.Logic.Test.Services
{
    internal class GenericTests
    {
        #region Get

        internal static void Get_GetEntityById_ThrowException<TFactory, TService, TDto, TDtoPost, TDtoEdit>(
                List<Action<IAppUnitOfWork>> populateDataActions)
            where TFactory : IServiceFactory<TService>
            where TService : ITestableGenericService<TDto, TDtoPost, TDtoEdit>, ITestableService
        {
            //Arrange
            var service = Activator.CreateInstance<TFactory>().CreateService();
            var unitOfWork = service.AppUnitOfWork;
            populateDataActions.ForEach(x => x(unitOfWork));

            // Act & Assert
            Assert.ThrowsAsync<EntityNotFoundException>(() => service.GetEntityById(Guid.NewGuid()));
        }

        internal static async Task Get_GetEntityById_Successful<TFactory, TService, TDto, TDtoPost, TDtoEdit, TEntity>(
                List<Action<IAppUnitOfWork>> populateDataActions,
                TEntity checkEntity)
            where TFactory : IServiceFactory<TService>
            where TService : ITestableGenericService<TDto, TDtoPost, TDtoEdit>, ITestableService
            where TEntity : BaseEntity
        {
            //Arrange
            var service = Activator.CreateInstance<TFactory>().CreateService();
            var unitOfWork = service.AppUnitOfWork;
            populateDataActions.ForEach(x => x(unitOfWork));

            // Act & Assert
            var dto = await service.GetEntityById(checkEntity.Id!.Value);

            ClassicAssert.IsTrue(ReflectionHelper.AreObjectsEqualShallowCompare(dto, checkEntity));
        }

        #endregion Get

        #region Get list

        internal static async Task GetList_CheckIfEmpty
            <TFactory, TService, TDto, TDtoPost, TDtoEdit>(
            IList<Action<IAppUnitOfWork>> populateDataActions,
            IList<FilterItem>? filterItems = null)
         where TFactory : IServiceFactory<TService>
         where TService : ITestableGenericService<TDto, TDtoPost, TDtoEdit>, ITestableService
        {
            //Arrange
            var service = Activator.CreateInstance<TFactory>().CreateService();
            var unitOfWork = service.AppUnitOfWork;
            populateDataActions.ForEach(x => x(unitOfWork));

            IList<SortItem>? sortedItems = null;
            int? pageIndex = null;
            int? pageSize = null;

            // Act
            var result = await service.GetEntities(
                filterItems,
                sortedItems,
                pageIndex,
                pageSize);

            //Assert
            Assert.That(result.Items.Count(), Is.EqualTo(0));
        }

        internal static async Task GetList_WithFilter_Successful<TFactory, TService, TDto, TDtoPost, TDtoEdit>(
            IList<FilterItem> filterItems,
            IList<Action<IAppUnitOfWork>> populateDataActions,
            IList<Func<EntityListDto<TDto>, bool>> checks)
            where TFactory : IServiceFactory<TService>
            where TService : ITestableGenericService<TDto, TDtoPost, TDtoEdit>, ITestableService
        {
            //Arrange
            var service = Activator.CreateInstance<TFactory>().CreateService();
            var unitOfWork = service.AppUnitOfWork;
            populateDataActions.ForEach(x => x(unitOfWork));

            IList<SortItem>? sortedItems = null;
            int? pageIndex = null;
            int? pageSize = null;

            // Act
            EntityListDto<TDto> result = await service.GetEntities(
                filterItems,
                sortedItems,
                pageIndex,
                pageSize);

            // Assert
            ClassicAssert.IsNotNull(result);
            ClassicAssert.IsTrue(result.Count > 0);

            if (checks != null)
            {
                checks.ForEach(x => ClassicAssert.IsTrue(x(result)));
            }
        }

        #endregion Get list

        #region Save

        internal static async Task Save_Successful<TFactory, TService, TDto, TDtoPost, TDtoEdit>(
            List<Action<IAppUnitOfWork>> populateDataActions,
            TDtoPost dtoPost)
            where TFactory : IServiceFactory<TService>
            where TService : ITestableGenericService<TDto, TDtoPost, TDtoEdit>, ITestableService
        {
            //Arrange
            TService service = Activator.CreateInstance<TFactory>().CreateService();
            var unitOfWork = service.AppUnitOfWork;
            populateDataActions.ForEach(x => x(unitOfWork));

            // Act
            TDto result = await service.SaveEntity(dtoPost);

            // Assert

            ClassicAssert.IsNotNull(result);
            ClassicAssert.IsTrue(ReflectionHelper.AreObjectsEqualShallowCompare(result, dtoPost));
        }

        internal static void Save_ValidationException<TFactory, TService, TDto, TDtoPost, TDtoEdit>(
            List<Action<IAppUnitOfWork>> populateDataActions,
            TDtoPost dtoPost)
            where TFactory : IServiceFactory<TService>
            where TService : ITestableGenericService<TDto, TDtoPost, TDtoEdit>, ITestableService
        {
            //Arrange
            TService service = Activator.CreateInstance<TFactory>().CreateService();
            var unitOfWork = service.AppUnitOfWork;
            populateDataActions.ForEach(x => x(unitOfWork));

            // Act & Assert
            Assert.ThrowsAsync<FluentValidation.ValidationException>(() => service.SaveEntity(dtoPost));
        }

        #endregion Save

        #region Delete

        internal static async Task Delete_Successful<TFactory, TService, TDto, TDtoPost, TDtoEdit>
            (List<Action<IAppUnitOfWork>> populateDataActions, string deleteEntityId)
            where TFactory : IServiceFactory<TService>
            where TService : ITestableGenericService<TDto, TDtoPost, TDtoEdit>, ITestableService
        {
            //Arrange
            TService service = Activator.CreateInstance<TFactory>().CreateService();
            var unitOfWork = service.AppUnitOfWork;
            populateDataActions.ForEach(x => x(unitOfWork));

            // Act
            await service.DeleteEntity(Guid.Parse(deleteEntityId));

            // Assert
            ClassicAssert.IsTrue(true);
        }

        internal static void Delete_Unsuccessful<TFactory, TService, TDto, TDtoPost, TDtoEdit>
            (List<Action<IAppUnitOfWork>> populateDataActions, string deleteEntityId)
            where TFactory : IServiceFactory<TService>
            where TService : ITestableGenericService<TDto, TDtoPost, TDtoEdit>, ITestableService
        {
            //Arrange
            TService service = Activator.CreateInstance<TFactory>().CreateService();
            var unitOfWork = service.AppUnitOfWork;
            populateDataActions.ForEach(x => x(unitOfWork));

            // Act & Assert
            Assert.ThrowsAsync<EntityNotFoundException>(() => service.DeleteEntity(
                Guid.Parse(deleteEntityId)));
        }

        #endregion Delete

        #region Helpers

        internal static void SaveObjects<TEntity, TRepository>(
            IEnumerable<TEntity> entities,
            TRepository repository,
            IAppUnitOfWork AppUnitOfWork,
            IUserContextReader userContextReader)
            where TEntity : BaseEntity
            where TRepository : IRepository<TEntity>
        {
            entities.ForEach(x => repository.AddEntity(x, userContextReader));
            AppUnitOfWork.SaveChanges();
            entities.ForEach(x => ((ITestableRepository<TEntity>)repository).DetachEntity(x));
        }

        internal static void DetachEntity<TEntity, TRepository>(
            TEntity entity,
            TRepository repository)
            where TEntity : BaseEntity
            where TRepository : IRepository<TEntity>
        {
            ((ITestableRepository<TEntity>)repository).DetachEntity(entity);
        }

        #endregion Helpers
    }
}