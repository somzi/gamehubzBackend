namespace Template.Logic.Utility
{
    public class ReflectionHelper
    {
        public static bool IsInterfaceImplemented<T, TInterfaceImplementation>()
        {
            return IsInterfaceImplemented<TInterfaceImplementation>(typeof(T));
        }

        public static bool IsInterfaceImplemented<TInterfaceImplementation>(Type typeToCheck)
        {
            return typeToCheck.GetInterfaces().Any(x => x == typeof(TInterfaceImplementation));
        }

        public static bool AreObjectsEqualShallowCompareSameObjects<TDto>(TDto dto1, TDto dto2)
        {
            foreach (var property in typeof(TDto)
                .GetProperties(System.Reflection.BindingFlags.Public))
            {
                if (property.PropertyType.IsValueType)
                {
                    if (property.GetValue(dto1) != property.GetValue(dto2))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        public static bool AreObjectsEqualShallowCompare<T1, T2>(T1 obj1, T2 obj2)
        {
            foreach (var property in typeof(T1)
                .GetProperties(System.Reflection.BindingFlags.Public))
            {
                if (!property.PropertyType.IsValueType)
                {
                    continue;
                }

                var property2 = typeof(T2).GetProperty(property.Name);

                if (property2 == null)
                {
                    continue;
                }

                if (property.GetValue(obj1) != property2.GetValue(obj2))
                {
                    return false;
                }
            }

            return true;
        }
    }
}