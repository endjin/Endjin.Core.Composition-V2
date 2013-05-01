﻿namespace Endjin.Core.Specs.Helpers
{
    #region Using statements

    using Moq;

    using NUnit.Framework;

    using SpecsFor;

    using StructureMap;
    using StructureMap.AutoMocking;

    using System;
    using System.Collections.Generic;
    using System.Linq;

    #endregion

    [TestFixture]
    public abstract class SpecificationsFor<T> : ITestStateWithContext<T> where T : class
    {
        protected readonly List<IContext<T>> Contexts = new List<IContext<T>>();
        protected MoqAutoMocker<T> Mocker;

        public T SUT { get; set; }

        protected SpecificationsFor()
        {
        }

        protected SpecificationsFor(Type[] contexts)
        {
            this.Given(contexts);
        }

        private void TryDisposeSUT()
        {
            if (this.SUT == null || !((object)this.SUT is IDisposable))
                return;
            ((IDisposable)this.SUT).Dispose();
        }

        public TContextType GetContext<TContextType>() where TContextType : IContext<T>
        {
            return (TContextType)this.Contexts.FirstOrDefault(c => c.GetType() == typeof(TContextType));
        }

        public TContextType GetContext<TContextType>(Func<IEnumerable<TContextType>, TContextType> search) where TContextType : IContext<T>
        {
            return search((IEnumerable<TContextType>)this.Contexts.Where(c => c.GetType() == typeof(TContextType)));
        }

        public Mock<TMock> GetMockFor<TMock>() where TMock : class
        {
            return Mock.Get(this.Mocker.Get<TMock>());
        }

        [TestFixtureSetUp]
        public virtual void SetupEachSpec()
        {
            this.Mocker = new MoqAutoMocker<T>();
            this.ConfigureContainer(this.Mocker.Container);
            this.InitializeClassUnderTest();
            try
            {
                this.GivenCore();
                this.WhenCore();
            }
            catch (Exception)
            {
                this.TryDisposeSUT();
                throw;
            }
        }

        protected virtual void InitializeClassUnderTest()
        {
            this.SUT = this.Mocker.ClassUnderTest;
        }

        protected virtual void ConfigureContainer(IContainer container)
        {
        }

        [TestFixtureTearDown]
        public virtual void TearDown()
        {
            try
            {
                this.AfterEachSpec();
            }
            finally
            {
                this.TryDisposeSUT();
            }
        }
       
        protected virtual void AfterEachSpec()
        {
        }

        protected abstract void EstablishContext();
        protected abstract void When();

        protected TContext Given<TContext>() where TContext : IContext<T>, new()
        {
            var context = new TContext(); 
            this.Contexts.Add(context);
            return context;
        }

        protected TContext And<TContext>() where TContext : IContext<T>, new()
        {
            return this.Given<TContext>();
        }

        protected void Given(IEnumerable<Type> context)
        {
            this.Contexts.AddRange(context.Select(Activator.CreateInstance).Cast<IContext<T>>());
        }

        private void GivenCore()
        {
            this.EstablishContext();
            this.Contexts.ForEach(c => c.Initialize(this));
        }

        private void WhenCore()
        {
            this.When();
        }
    }

    public abstract class Specifications : SpecificationsFor<object>
    {
    }
}