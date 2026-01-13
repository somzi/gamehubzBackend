namespace GameHubz.Logic
{
    public class BaseService
    {
        internal BaseService(IUnitOfWork unitOfWork)
        {
            this.UnitOfWork = unitOfWork;
        }

        protected IUnitOfWork UnitOfWork { get; private set; }

        internal async Task SaveAsync()
        {
            this.Saving();
            await this.SaveInternalAsync();
            this.Saved();
        }

        protected virtual async Task SaveInternalAsync()
        {
            await this.UnitOfWork.SaveChangesAsync();
        }

        protected virtual void Saving()
        {
        }

        protected virtual void Saved()
        {
        }
    }
}
