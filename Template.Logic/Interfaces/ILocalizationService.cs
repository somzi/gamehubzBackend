namespace Template.Logic.Interfaces
{
    public interface ILocalizationService
    {
        string this[string key] { get; }

        string PropertyIsEmptyMessage(string propertyName);

        string CannotDeleteEntityAlreadyAddedTo<TChild, TParent>(Guid childEntityId);

        string PropertyValueAlreadyExists(string objectName, string propertyName);
    }
}