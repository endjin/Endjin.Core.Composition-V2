﻿namespace Endjin.Core.Container
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Globalization;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;

    public class Container : IContainer
    {
        private readonly Dictionary<Type, List<ComponentRegistration>> componentsByType = new Dictionary<Type, List<ComponentRegistration>>();
        private readonly Dictionary<string, List<ComponentRegistration>> componentsByName = new Dictionary<string, List<ComponentRegistration>>();
        private readonly List<ComponentRegistration> allRegistrations = new List<ComponentRegistration>();
        private readonly Dictionary<Type, object> singletons = new Dictionary<Type, object>();
        private readonly List<ComponentRegistration> misconfiguredComponents = new List<ComponentRegistration>();
        private readonly ReaderWriterLockSlim singletonLock = new ReaderWriterLockSlim(LockRecursionPolicy.SupportsRecursion);

        private bool isInstalling;

        public IEnumerable<KeyValuePair<Type, List<ComponentRegistration>>> ComponentsByType
        {
            get
            {
                return this.componentsByType;
            }
        }

        public IEnumerable<KeyValuePair<string, List<ComponentRegistration>>> ComponentsByName
        {
            get
            {
                return this.componentsByName;
            }
        }

        public ReadOnlyCollection<ComponentRegistration> MisconfiguredComponents
        {
            get
            {
                return new ReadOnlyCollection<ComponentRegistration>(this.misconfiguredComponents);
            }
        }

#if NET40
        public Task InstallAsync(IBootstrapper bootstrapper)
        {
            this.isInstalling = true;

            return Task.Factory.StartNew(
                () =>
                    {

                        var installers = bootstrapper.GetInstallersAsync().Result;
                        foreach (var installer in installers)
                        {
                            installer.Install(this);
                        }

                        this.FindConstructors(this.allRegistrations);
                    }).ContinueWith(t => this.isInstalling = false);
        }
#else
        public async Task InstallAsync(IBootstrapper bootstrapper)
        {
            this.isInstalling = true;

            try
            {
                var installers = await bootstrapper.GetInstallersAsync();
                foreach (var installer in installers)
                {
                    installer.Install(this);
                }
            }
            finally
            {
                this.isInstalling = false;
            }

            this.FindConstructors(this.allRegistrations);
        }
#endif
     
        public void Shutdown()
        {
            singletonLock.EnterWriteLock();

            try
            {
                var disposables = new HashSet<IDisposable>();

                foreach (var component in this.singletons.Values.Where(s => s is IDisposable).Cast<IDisposable>())
                {
                    DisposeSafely(disposables, component);
                }

                foreach (var component in this.allRegistrations.Where(s => s.ComponentInstance is IDisposable).Select(s => s.ComponentInstance).Cast<IDisposable>())
                {
                    DisposeSafely(disposables, component);
                }

                this.allRegistrations.Clear();
                this.componentsByName.Clear();
                this.componentsByType.Clear();
                this.singletons.Clear();
            }
            finally
            {
                singletonLock.ExitWriteLock();
            }
        }

        public bool HasComponent<T>()
        {
            return this.HasComponent(typeof(T));
        }

        public bool HasComponent(Type type)
        {
            var registration = this.GetComponentRegistrationFor(type);
            return registration != null;
        }

        public T Resolve<T>()
        {
            return (T)this.Resolve(typeof(T));
        }

        public T TryResolve<T>()
        {
            return (T)this.TryResolve(typeof(T));
        }

        public object TryResolve(Type type)
        {
            var error = new ResolutionTreeNode { Component = "root", Children = new List<ResolutionTreeNode>() };
            var result = this.ResolveCore(type, error);

            if (error.IsInError)
            {
                return null;
            }

            return result;
        }

        public T Resolve<T>(Type type)
        {
            return (T)this.Resolve(type);
        }

        public IEnumerable<T> ResolveAll<T>()
        {
            var registrations = this.GetComponentRegistrationsFor(typeof(T));
            return this.GetInstancesFor(registrations).Cast<T>();
        }

        public IEnumerable<object> ResolveAll(Type type)
        {
            var componentRegistration = this.GetComponentRegistrationsFor(type);
            return this.GetInstancesFor(componentRegistration);
        }

        public IEnumerable<T> ResolveAll<T>(string name)
        {
            var registrations = this.GetComponentRegistrationsFor(typeof(T), name);
            return this.GetInstancesFor(registrations).Cast<T>();
        }

        public IEnumerable<object> ResolveAll(Type type, string name)
        {
            var componentRegistration = this.GetComponentRegistrationsFor(type, name);
            return this.GetInstancesFor(componentRegistration);
        }

        public T Resolve<T>(string name)
        {
            var componentRegistration = this.GetComponentRegistrationFor(typeof(T), name);
            var error = new ResolutionTreeNode { Component = "root", Children = new List<ResolutionTreeNode>() };
            var result = (T)this.GetInstanceFor(componentRegistration, name, error);

            if (error.IsInError)
            {
                throw new ContainerException(error, this.BuildMessage(error));
            }

            return result;
        }

        public object Resolve(string name)
        {
            var componentRegistration = this.GetComponentRegistrationFor(null, name);
            var error = new ResolutionTreeNode { Component = "root", Children = new List<ResolutionTreeNode>() };
            var result = this.GetInstanceFor(componentRegistration, name, error);

            if (error.IsInError)
            {
                throw new ContainerException(error, this.BuildMessage(error));
            }

            return result;
        }

        public object Resolve(Type type)
        {
            var error = new ResolutionTreeNode { Component = "root", Children = new List<ResolutionTreeNode>() };
            var result = this.ResolveCore(type, error);
            if (error.IsInError)
            {
                throw new ContainerException(error, this.BuildMessage(error));
            }

            return result;
        }

        public void Register(IEnumerable<ComponentRegistration> registrations)
        {
            var componentRegistrations = registrations as List<ComponentRegistration> ?? registrations.ToList();
            this.PerformInitialRegistration(componentRegistrations);

            if (!this.isInstalling)
            {
                this.FindConstructors(this.allRegistrations);
            }
        }

        private static void DisposeSafely(ISet<IDisposable> disposables, IDisposable component)
        {
            if (!disposables.Contains(component))
            {
                component.Dispose();
                disposables.Add(component);
            }
        }

        private static List<ComponentRegistration> EnsureRegistrations<T>(IDictionary<T, List<ComponentRegistration>> dictionary, T key)
        {
            List<ComponentRegistration> result;
            if (!dictionary.TryGetValue(key, out result))
            {
                result = new List<ComponentRegistration>();
                dictionary.Add(key, result);
            }

            return result;
        }

        private static void AddTabs(StringBuilder str, int tabs)
        {
            if (tabs > 0)
            {
                str.Append(new string('\t', tabs));
            }
        }

        private string BuildMessage(ResolutionTreeNode error)
        {
            var str = new StringBuilder();
            this.BuildMessageCore(error.Children.Where(e => e.IsInError), str, 0);
            return str.ToString();
        }

        private void BuildMessageCore(IEnumerable<ResolutionTreeNode> children, StringBuilder str, int tabs)
        {
            foreach (var node in children)
            {
                AddTabs(str, tabs);
                str.AppendFormat(CultureInfo.CurrentCulture, "Unable to resolve component for '{0}' ", node.Component);

                var childrenInError = node.Children.Where(e => e.IsInError);

                var resolutionTreeNodes = childrenInError as ResolutionTreeNode[] ?? childrenInError.ToArray();

                if (resolutionTreeNodes.Any())
                {
                    str.AppendLine("because its dependencies have the following errors:");
                    this.BuildMessageCore(resolutionTreeNodes, str, tabs + 1);
                }
                else
                {
                    str.AppendLine("because no instance is registered for that component.");
                }
            }
        }

        private object ResolveCore(Type type, ResolutionTreeNode parentTreeNode)
        {
            var componentRegistration = this.GetComponentRegistrationFor(type);
            return this.GetInstanceFor(componentRegistration, type.Name, parentTreeNode);
        }

        private IEnumerable<object> GetInstancesFor(IEnumerable<ComponentRegistration> registrations)
        {
            var parentError = new ResolutionTreeNode { Component = null, Children = new List<ResolutionTreeNode>(), IsInError = false };
            return (from r in registrations select this.GetInstanceFor(r, r.ComponentType.Name, parentError)).Where(o => o != null).ToArray();
        }

        private object GetInstanceFor(ComponentRegistration registration, string name, ResolutionTreeNode parentTreesNode)
        {
            object instance;

            var node = new List<ResolutionTreeNode>();
            var error = new ResolutionTreeNode { Component = name, Children = node };
            parentTreesNode.Children.Add(error);

            if (registration == null)
            {
                parentTreesNode.IsInError = true;
                error.IsInError = true;
                return null;
            }

            if (registration.Lifestyle == Lifestyle.Singleton)
            {
                if (registration.ComponentInstance != null)
                {
                    return registration.ComponentInstance;
                }

                this.singletonLock.EnterWriteLock();

                try
                {
                    if (!this.singletons.TryGetValueByTypeFullName(registration.ComponentType, out instance))
                    {
                        instance = this.CreateInstanceFor(registration, error);
                        if (instance != null)
                        {
                            this.singletons.Add(registration.ComponentType, instance);
                        }
                    }
                }
                finally
                {
                    this.singletonLock.ExitWriteLock();
                }
            }
            else
            {
                instance = this.CreateInstanceFor(registration, error);
            }

            if (error.IsInError)
            {
                parentTreesNode.IsInError = true;
            }

            return instance;
        }

        private object CreateInstanceFor(ComponentRegistration registration, ResolutionTreeNode parentTreeNode)
        {
            if (registration.PreferredConstructor == null)
            {
                parentTreeNode.Children.Add(new ResolutionTreeNode { Component = registration.ComponentType.Name, IsInError = true, Children = new List<ResolutionTreeNode>()});
                parentTreeNode.IsInError = true;
                return null;
            }

            var parameters = registration.PreferredConstructor.Parameters.Select(p => this.ResolveCore(p.ParameterType, parentTreeNode)).ToArray();

            if (parentTreeNode.IsInError)
            {
                return null;
            }

            return Activator.CreateInstance(registration.ComponentType, parameters);
        }

        private void FindConstructors(IEnumerable<ComponentRegistration> registrations)
        {
            this.misconfiguredComponents.Clear();

            foreach (var registration in registrations)
            {
                var constructorRegistrations = registration.ComponentType.GetTypeInfo().GetConstructors().Select(c => new ConstructorRegistration { Constructor = c, Parameters = c.GetParameters() }).OrderByDescending(r => r.Parameters.Length);
                var resolvedConstructor = constructorRegistrations.FirstOrDefault(this.ResolveConstructor);
                if (resolvedConstructor != null)
                {
                    registration.PreferredConstructor = resolvedConstructor;
                    registration.Error = string.Empty;

                    if (this.misconfiguredComponents.Contains(registration))
                    {
                        this.misconfiguredComponents.Remove(registration);
                    }
                }
                else
                {
                    registration.PreferredConstructor = constructorRegistrations.FirstOrDefault();
                    var error = new ResolutionTreeNode { Component = null, Children = new List<ResolutionTreeNode>(), IsInError = false };
                    this.GetInstanceFor(registration, registration.ComponentType.Name, error);
                    registration.Error = this.BuildMessage(error);
                    this.misconfiguredComponents.Add(registration);
                }
            }
        }

        private bool ResolveConstructor(ConstructorRegistration constructor)
        {
            var parameters = constructor.Parameters;
            return parameters.Length == 0 || parameters.All(this.CanResolve);
        }

        private bool CanResolve(ParameterInfo parameter)
        {
            return this.GetComponentRegistrationFor(parameter.ParameterType) != null;
        }

        private ComponentRegistration GetComponentRegistrationFor(Type parameterType)
        {
            var registrations = this.GetComponentRegistrationsFor(parameterType);
            return registrations == null ? null : registrations.FirstOrDefault();
        }

        private ComponentRegistration GetComponentRegistrationFor(Type parameterType, string name)
        {
            var registrations = this.GetComponentRegistrationsFor(parameterType, name);

            if (registrations == null)
            {
                return null;
            }

            var componentRegistrations = registrations as List<ComponentRegistration> ?? registrations.ToList();

            if (componentRegistrations.Count == 0)
            {
                return null;
            }

            return componentRegistrations.First();
        }

        private IEnumerable<ComponentRegistration> GetComponentRegistrationsFor(Type parameterType)
        {
            List<ComponentRegistration> registrations;
            if (this.componentsByType.TryGetValueByTypeFullName(parameterType, out registrations))
            {
                if (registrations.Count > 0)
                {
                    return registrations;
                }
            }

            return (from key in this.componentsByType.Keys
                    where key.GetTypeInfo().IsAssignableFrom(parameterType.GetTypeInfo())
                    select this.componentsByType[key]).FirstOrDefault(k => k.Count > 0);
        }

        private IEnumerable<ComponentRegistration> GetComponentRegistrationsFor(Type type, string name)
        {
            List<ComponentRegistration> registrations;
            if (this.componentsByName.TryGetValue(name, out registrations))
            {
                if (registrations.Count > 0)
                {
                    if (type != null)
                    {
                        var typeInfo = type.GetTypeInfo();
                        return (from r in registrations
                                where
                                    r.RegistrationTypes.Any(
                                        t => typeInfo.IsAssignableFrom(t.GetTypeInfo()))
                                select r).ToArray();
                    }

                    return registrations;
                }
            }

            return null;
        }

        private void PerformInitialRegistration(IEnumerable<ComponentRegistration> registrations)
        {
            foreach (var registration in registrations)
            {
                this.AddByType(registration);
                this.AddByName(registration);
                this.allRegistrations.Add(registration);
            }
        }

        private void AddByType(ComponentRegistration registration)
        {
            foreach (var type in registration.RegistrationTypes)
            {
                var registrations = this.EnsureTypeRegistrations(type);
                registrations.Add(registration);
            }
        }

        private void AddByName(ComponentRegistration registration)
        {
            var registrations = this.EnsureNameRegistrations(registration.Name ?? registration.ComponentType.Name);
            registrations.Add(registration);
        }

        private List<ComponentRegistration> EnsureTypeRegistrations(Type type)
        {
            return EnsureRegistrations(this.componentsByType, type);
        }

        private List<ComponentRegistration> EnsureNameRegistrations(string name)
        {
            return EnsureRegistrations(this.componentsByName, name);
        }
    }
}