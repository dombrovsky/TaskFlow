namespace System.Threading.Tasks.Flow.Internal
{
    using System;

    internal sealed class AnnotationScope
    {
        private readonly AnnotationScope? _parent;
        private readonly Type _type;
        private readonly IOperationAnnotation _annotation;

        public AnnotationScope(AnnotationScope? parent, Type type, IOperationAnnotation annotation)
        {
            _parent = parent;
            _type = type;
            _annotation = annotation;
        }

        public T? Get<T>() where T : class, IOperationAnnotation
        {
            for (var current = this; current != null; current = current._parent)
            {
                if (current._type == typeof(T)) return (T)current._annotation;
            }

            return null;
        }

        public IOperationAnnotation? Get(Type type)
        {
            for (var current = this; current != null; current = current._parent)
            {
                if (current._type == type) return current._annotation;
            }

            return null;
        }
    }
}
