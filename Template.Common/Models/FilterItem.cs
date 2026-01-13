using Template.Common.Enums;

namespace Template.Common.Models
{
    public class FilterItem
    {
        public FilterItem()
        {
            this.ExpressionOperator = ExpressionOperator.Equals;
            this.LogicalOperator = FilterLogicalOperator.AND;
            this.Value = "";
            this.PropertyName = "";
        }

        public string PropertyName { get; set; }

        public string Value { get; set; }

        public ExpressionOperator ExpressionOperator { get; set; }

        public FilterLogicalOperator LogicalOperator { get; set; }
    }
}