﻿using DotNetStarter.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Renderings
{
    /// <summary>
    /// Default implementation fro IRenderingAliasResolver
    /// </summary>
    [Registration(typeof(IRenderingAliasResolver), Lifecycle.Singleton)]
    public class RenderingAliasResolver : IRenderingAliasResolver
    {
        private static readonly Type _AttrRenderingAliasType = typeof(RenderingDocumentAliasAttribute);

        private readonly IReflectionHelper _ReflectionHelper;
        private readonly IStartupConfiguration _StartupConfiguration;

        /// <summary>
        /// Backing field, do not use directly, always use EnsureRenderingModels to ensure creation
        /// </summary>
        private Dictionary<string, ResolveResult> _RenderingModels;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="startupConfig"></param>
        /// <param name="reflectionHelper"></param>
        public RenderingAliasResolver(IStartupConfiguration startupConfig, IReflectionHelper reflectionHelper)
        {
            _StartupConfiguration = startupConfig;
            _ReflectionHelper = reflectionHelper;
        }

        private Dictionary<string, ResolveResult> EnusreRenderingModels
        {
            get
            {
                if (_RenderingModels == null)
                {
                    _RenderingModels = new Dictionary<string, ResolveResult>();
                    var discoveredRenderings = _StartupConfiguration.AssemblyScanner.GetTypesFor(_AttrRenderingAliasType);

                    foreach (var type in discoveredRenderings)
                    {
                        var attr = _ReflectionHelper.GetCustomAttribute(type, _AttrRenderingAliasType, false)
                            .OfType<RenderingDocumentAliasAttribute>()
                            .FirstOrDefault();

                        if (attr != null)
                        {
                            _RenderingModels[attr.DocumentAlias] = new ResolveResult(type, attr, attr.DocumentAlias);
                        }
                    }
                }

                return _RenderingModels;
            }
        }

        /// <summary>
        /// Resolves a string alias to a ResolveResult which may have errors
        /// </summary>
        /// <param name="documentAlias"></param>
        /// <returns></returns>
        public virtual ResolveResult Resolve(string documentAlias)
        {
            if (EnusreRenderingModels.TryGetValue(documentAlias, out ResolveResult match))
            {
                return match;
            }

            TryThrow(documentAlias);

            return new ResolveResult(alias: documentAlias);
        }

        /// <summary>
        /// Resolve a type from a document alias
        /// </summary>
        /// <param name="documentAlias"></param>
        /// <returns></returns>
        public virtual Type ResolveAlias(string documentAlias)
        {
            return Resolve(documentAlias).ModelType;
        }

        /// <summary>
        /// Resolves many types from many aliases
        /// </summary>
        /// <param name="documentAliases"></param>
        /// <returns></returns>
        public virtual IEnumerable<Type> ResolveAliases(ICollection<string> documentAliases)
        {
            foreach (var alias in documentAliases)
            {
                var result = Resolve(alias);

                if (result.HasErrors == false)
                {
                    yield return result.ModelType;
                }
            }

            yield break;
        }

        /// <summary>
        /// Resolves a property alias from given expression
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="expression"></param>
        /// <returns></returns>
        public virtual string ResolvePropertyAlias<T>(Expression<Func<T, object>> expression)
        {
            var member = ResolveMemberInfo(expression);

            return GetPropertyAttributeValue<RenderingPropertyAliasAttribute, string>(member, attr => attr.PropertyAlias);
        }

        /// <summary>
        /// Resolves a string alias from given type
        /// </summary>
        /// <param name="renderingModelType"></param>
        /// <returns></returns>
        public virtual string ResolveType(Type renderingModelType)
        {
            return ResolveTypes(new Type[] { renderingModelType }).FirstOrDefault();
        }

        /// <summary>
        /// Resolves many types to many string aliases
        /// </summary>
        /// <param name="types"></param>
        /// <returns></returns>
        public virtual IEnumerable<string> ResolveTypes(ICollection<Type> types)
        {
#if !NETSTANDARD1_0
            var filteredTypes = types.Where(t => !t.IsAbstract && !t.IsInterface).ToList();
#else
            var filteredTypes = types.Where(t =>
            {
                var tinfo = t.GetTypeInfo();

                return !tinfo.IsAbstract && !tinfo.IsInterface;
            }).ToList();

#endif
            var matches = EnusreRenderingModels.Where(x => filteredTypes.Any(y => y == x.Value.ModelType)).ToList();

            if (matches.Count != filteredTypes.Count)
            {
                TryThrow(string.Join(",", types.Select(t => t.FullName)));
            }

            foreach (var match in matches)
            {
                yield return match.Key;
            }

            yield break;
        }

        /// <summary>
        /// Gets a given attribute's value for memberinfo
        /// </summary>
        /// <typeparam name="TAttribute"></typeparam>
        /// <typeparam name="TValue"></typeparam>
        /// <param name="memberInfo"></param>
        /// <param name="valueSelector"></param>
        /// <returns></returns>
        protected static TValue GetPropertyAttributeValue<TAttribute, TValue>(MemberInfo memberInfo, Func<TAttribute, TValue> valueSelector) where TAttribute : Attribute
        {
            return memberInfo.GetCustomAttributes(typeof(TAttribute), true).FirstOrDefault() is TAttribute attr ? valueSelector(attr) :
                throw new Exception($"Unable to find attribute {typeof(TAttribute).FullName} on {memberInfo.Name}");// default(TValue);
        }

        /// <summary>
        /// Gets member info from an expression
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="expression"></param>
        /// <returns></returns>
        protected static MemberInfo ResolveMemberInfo<T>(Expression<Func<T, object>> expression)
        {
            MemberInfo memberInfo = null;

            if (expression.Body is MemberExpression memberExpression)
            {
                memberInfo = memberExpression.Member;
            }
            else
            {
                var op = ((UnaryExpression)expression.Body).Operand;
                memberInfo = ((MemberExpression)op).Member;
            }

            if (memberInfo == null)
                throw new ArgumentException("Unable to resolve member info for " + typeof(T).FullName);

            return memberInfo;
        }

        private void ThrowMissingViewModel(string aliases)
        {
            throw new Exception("Unable to resolve a model for alias(es)/type(s): " + aliases + ". Please check your view models!");
        }

        private bool TryThrow(string alias)
        {
            var environment = _StartupConfiguration.Environment;

            if (environment == null || environment.IsProduction() == true)
            {
                return false;
            }

            ThrowMissingViewModel(alias);
            return true;
        }
    }
}