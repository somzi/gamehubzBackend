using FluentMigrator.Builders.Alter.Table;
using FluentMigrator.Builders.Create;
using FluentMigrator.Builders.Create.Table;

namespace GameHubz.DataMigrations
{
    public static class MigratorExtensions
    {
        private const string MaxStringType = "nvarchar(max)";

        public static ICreateTableWithColumnSyntax TableWithCommonColumns(this ICreateExpressionRoot create, string tableName)
        {
            return create.Table(tableName)
               .WithColumn("Id").AsGuid().PrimaryKey()
               .WithColumn("CreatedOn").AsDateTime().NotNullable()
               .WithColumn("ModifiedOn").AsDateTime().NotNullable()
               .WithColumn("CreatedBy").AsGuid().Nullable()
               .WithColumn("ModifiedBy").AsGuid().Nullable()
               .WithColumn("IsDeleted").AsBoolean().NotNullable();
        }

        public static IAlterTableColumnOptionOrAddColumnOrAlterColumnSyntax AsMaxString(this IAlterTableColumnAsTypeSyntax alterTableColumnAsTypeSyntax)
        {
            return alterTableColumnAsTypeSyntax.AsCustom(MaxStringType);
        }

        public static ICreateTableColumnOptionOrWithColumnSyntax AsMaxString(this ICreateTableColumnAsTypeSyntax createTableColumnAsTypeSyntax)
        {
            return createTableColumnAsTypeSyntax.AsCustom(MaxStringType);
        }
    }
}
