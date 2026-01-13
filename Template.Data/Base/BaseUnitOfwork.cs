using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Template.Common;
using Template.Common.Enums;
using Template.Common.Interfaces;
using Template.Data.Extensions;
using Template.Logic.Interfaces;
using Template.Logic.Utility;

namespace Template.Data
{
    public class BaseUnitOfWork : IDisposable, IUnitOfWork
    {
        private Dictionary<Type, BaseRepository> RepositoriesInternal { get; set; }
        private readonly DateTimeProvider dateTimeProvider;
        private readonly IFilterExpressionBuilder filterExpressionBuilder;
        private readonly ISortStringBuilder sortStringBuilder;
        private readonly ILocalizationService localizationService;
        private bool disposed;
        protected DbContext Context { get; private set; }

        public BaseUnitOfWork(
            DbContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
        {
            this.Context = context;
            this.dateTimeProvider = dateTimeProvider;
            this.RepositoriesInternal = new Dictionary<Type, BaseRepository>();
            this.filterExpressionBuilder = filterExpressionBuilder;
            this.sortStringBuilder = sortStringBuilder;
            this.localizationService = localizationService;
        }

        public void SaveChanges(bool setTimestamps = true)
        {
            if (setTimestamps)
            {
                this.SetTimestamps();
            }

            this.Context.SaveChanges();
        }

        public async Task SaveChangesAsync(bool setTimestamps = true)
        {
            if (setTimestamps)
            {
                this.SetTimestamps();
            }

            await this.Context.SaveChangesAsync();
        }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        public IReadOnlyDictionary<BaseEntity, AffectedEntityState> GetAffectedEntities()
        {
            Dictionary<BaseEntity, AffectedEntityState> result = new();

            foreach (EntityEntry entity in this.Context.ChangeTracker.Entries()
                        .Where(x =>
                            x.State == EntityState.Added
                            || x.State == EntityState.Modified
                            || x.State == EntityState.Deleted))
            {
                AffectedEntityState state;
                if (entity.State == EntityState.Added)
                {
                    state = AffectedEntityState.Added;
                }
                else if (entity.State == EntityState.Modified)
                {
                    state = AffectedEntityState.Updated;
                }
                else if (entity.State == EntityState.Deleted)
                {
                    state = AffectedEntityState.Deleted;
                }
                else
                {
                    throw new InvalidOperationException(Strings.BaseUnitOfWork_GetAffectedEntities_InvalidOperationException);
                }

                result.Add((BaseEntity)entity.Entity, state);
            }

            return result;
        }

        protected TRepository GetRepository<TRepository>()
            where TRepository : BaseRepository
        {
            Type type = typeof(TRepository);
            if (!this.RepositoriesInternal.ContainsKey(type))
            {
                if (Activator.CreateInstance(type,
                    new object[]
                    {
                        this.Context,
                        this.dateTimeProvider,
                        this.filterExpressionBuilder,
                        this.sortStringBuilder,
                        this.localizationService,
                    }) is not TRepository repository)
                {
                    throw new UnableToActivateRepositoryException();
                }

                this.RepositoriesInternal.Add(type, repository);
            }

            return (TRepository)this.RepositoriesInternal[type];
        }

        protected virtual void SetTimestamps()
        {
            DateTime now = this.dateTimeProvider.Now();

            IReadOnlyDictionary<BaseEntity, AffectedEntityState> affectedEntities = this.GetAffectedEntities();
            foreach (BaseEntity affectedEntity in affectedEntities.Keys)
            {
                affectedEntity.ModifiedOn = now;
                AffectedEntityState state = affectedEntities[affectedEntity];

                if (state == AffectedEntityState.Added)
                {
                    affectedEntity.CreatedOn = now;
                }
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (this.disposed)
            {
                return;
            }

            if (disposing)
            {
                this.Context.Dispose();
            }

            this.disposed = true;
        }
    }
}