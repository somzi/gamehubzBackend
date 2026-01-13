namespace Template.Logic.TestInterfaces
{
    public interface ITestableRepository<TEntity>
    {
        void DetachEntity(TEntity entity);
    }
}