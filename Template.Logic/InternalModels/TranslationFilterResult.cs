namespace Template.Logic.InternalModels
{
    internal class TranslationFilterResult
    {
        internal TranslationFilterResult()
        {
            this.FoundPropertiesButNoResult = false;
            this.EntityIds = new();
        }

        /// <summary>
        /// Check if any property was found and queried, but no result was found
        /// If yes, this means there are no results
        /// </summary>
        internal bool FoundPropertiesButNoResult { get; set; }

        internal List<Guid> EntityIds { get; set; }
    }
}