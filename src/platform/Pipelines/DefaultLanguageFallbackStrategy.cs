using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using Sitecore.Annotations;
using Sitecore.Data;
using Sitecore.Data.Items;
using Sitecore.Data.Managers;
using Sitecore.Diagnostics;
using Sitecore.Globalization;

namespace Sitecore.Demo.Edge.Website.Pipelines
{
    public class DefaultLanguageFallbackStrategy: LanguageFallbackStrategy
    {
        private readonly ConcurrentDictionary<string, LanguageMapping> _mappings = new ConcurrentDictionary<string, LanguageMapping>();
        private readonly object _syncOuterLock = new object();

        /// <summary>
        ///     Gets the fallback language.
        /// </summary>
        /// <param name="language">The language for which to get the fallback.</param>
        /// <param name="database">The database which defines the fallback policy.</param>
        /// <param name="relatedItemId">ID of the related item</param>
        /// <returns>
        ///     An instance of <see cref="Language" /> class which is the fallback of the specified language in the specified
        ///     database.
        /// </returns>
        public override Language GetFallbackLanguage(Language language, Database database, ID relatedItemId)
        {
            return !string.IsNullOrEmpty(language.Name)
                ? GetMapping(database).GetFallbackLanguage(language)
                : null;
        }

        /// <summary>
        ///     Gets the languages that depends on <paramref name="fallbackLanguage" />.
        /// </summary>
        /// <param name="fallbackLanguage">The fallback language.</param>
        /// <param name="database">The database.</param>
        /// <param name="relatedItemId">The related item identifier.</param>
        /// <returns></returns>
        public override List<Language> GetDependentLanguages(Language fallbackLanguage, Database database, ID relatedItemId)
        {
            return !string.IsNullOrEmpty(fallbackLanguage.Name)
                ? GetMapping(database).GetDependentLanguages(fallbackLanguage)
                : new List<Language>();
        }

        /// <summary>
        ///     Creates the language mapping.
        /// </summary>
        /// <returns></returns>
        [NotNull]
        protected virtual LanguageMapping CreateLanguageMapping()
        {
            return new LanguageMapping(_syncOuterLock);
        }

        /// <summary>
        ///     Gets the mapping for the specified database.
        /// </summary>
        /// <param name="database">The database for which to load mapping.</param>
        /// <returns>And instance of <see cref="LanguageMapping" /> class.</returns>
        [NotNull]
        protected LanguageMapping GetMapping([NotNull] Database database)
        {
            Assert.ArgumentNotNull(database, nameof(database));

            if (_mappings.TryGetValue(database.Name, out var mapping))
            {
                return mapping;
            }

            mapping = CreateLanguageMapping();
            _mappings[database.Name] = mapping;
            mapping.Load(database);

            return mapping;
        }

        /// <summary>
        ///     Represents language mapping for a single database
        /// </summary>
        protected class LanguageMapping
        {
            private static readonly List<Language> EmptyList = new List<Language>();

            private readonly Dictionary<string, List<Language>> _dependentLanguagesMapping = new Dictionary<string, List<Language>>();

            private readonly object _syncLock;

            /// <summary>
            ///     Internal table of the languages.
            /// </summary>
            private Dictionary<string, Language> _languages;

            /// <summary>
            ///     Initializes a new instance of the <see cref="LanguageMapping" /> class.
            /// </summary>
            public LanguageMapping()
            {
            }

            /// <summary>
            ///     Initializes a new instance of the <see cref="LanguageMapping" /> class.
            /// </summary>
            /// <param name="lockOwner">The lock owner.</param>
            public LanguageMapping(object lockOwner)
            {
                _syncLock = lockOwner;
            }

            /// <summary>
            ///     Gets the fallback language.
            /// </summary>
            /// <param name="language">The language.</param>
            /// <returns></returns>
            [CanBeNull]
            public virtual Language GetFallbackLanguage([NotNull] Language language)
            {
                Assert.ArgumentNotNull(language, nameof(language));

                if (_languages == null)
                {
                    return null;
                }

                if (_languages.TryGetValue(language.Name.ToLowerInvariant(), out var output))
                {
                    return output;
                }

                return null;
            }

            /// <summary>
            ///     Gets the dependent languages.
            /// </summary>
            /// <param name="language">The language.</param>
            /// <returns></returns>
            [NotNull]
            public virtual List<Language> GetDependentLanguages([NotNull] Language language)
            {
                Assert.ArgumentNotNull(language, nameof(language));

                if (_dependentLanguagesMapping == null)
                {
                    return EmptyList;
                }

                if (_dependentLanguagesMapping.TryGetValue(language.Name.ToLowerInvariant(), out var output))
                {
                    return output;
                }

                return EmptyList;
            }

            /// <summary>
            ///     Loads the specified database.
            /// </summary>
            /// <param name="database">The database.</param>
            public virtual void Load([NotNull] Database database)
            {
                Assert.ArgumentNotNull(database, nameof(database));

                lock (_syncLock)
                {
                    Language[] databaseLanguages = database.Languages;

                    Log.Warn($"--------------DefaultLanguageFallbackStrategy.Load #174 for {database.Name}.", this);

                    foreach (Language databaseLanguage in databaseLanguages)
                    {
                        Log.Warn($"--------------DefaultLanguageFallbackStrategy.Load #178 for {database.Name}, databaseLanguage.Name={databaseLanguage.Name}.", this);
                    }

                    Dictionary<string, Language> nameMapping = databaseLanguages.ToDictionary(language => language.Name.ToLowerInvariant());
                    var mapping = new Dictionary<string, Language>();

                    foreach (Language language in databaseLanguages)
                    {
                        if ((object)language.Origin.ItemId == null)
                        {
                            continue;
                        }

                        Item item = database.GetItem(language.Origin.ItemId);
                        if (item == null)
                        {
                            continue;
                        }

                        if (nameMapping.TryGetValue(item[FieldIDs.FallbackLanguage].ToLowerInvariant(), out var fallback))
                        {
                            mapping[item.Name.ToLowerInvariant()] = fallback;
                        }
                    }

                    if (_languages == null)
                    {
                        AttachEvents(database);
                    }

                    _languages = mapping;

                    LoadDependentLanguages(databaseLanguages);
                }
            }

            /// <summary>
            ///     Attaches the events.
            /// </summary>
            /// <param name="database">The database.</param>
            private void AttachEvents([NotNull] Database database)
            {
                Assert.ArgumentNotNull(database, nameof(database));

                string name = database.Name;

                database.Engines.DataEngine.SavedItem += (sender, e) => OnSavedItem(e.Command.Item);
                database.Engines.DataEngine.SavedItemRemote += (sender, e) => OnSavedItem(e.Item);

                Database.InstanceCreated += (source, args) =>
                {
                    if (args.Database.Name == name)
                    {
                        args.Database.Engines.DataEngine.SavedItem += (sender, e) => OnSavedItem(e.Command.Item);
                        args.Database.Engines.DataEngine.SavedItemRemote += (sender, e) => OnSavedItem(e.Item);
                    }
                };
            }

            private void OnSavedItem([NotNull] Item item)
            {
                if (item.TemplateID == TemplateIDs.Language)
                {
                    lock (_syncLock)
                    {
                        Load(item.Database);
                    }
                }
            }

            private void LoadDependentLanguages(IEnumerable<Language> databaseLanguages)
            {
                _dependentLanguagesMapping.Clear();

                foreach (string languageName in _languages.Values.Select(language => language.Name.ToLowerInvariant()).Distinct())
                {
                    var dependentLanguages = CalculateDependentLanguages(languageName).Select(s => databaseLanguages.First(language => language.Name.ToLowerInvariant() == s)).ToList();
                    _dependentLanguagesMapping.Add(languageName, dependentLanguages);
                }
            }

            private IEnumerable<string> CalculateDependentLanguages(string languageName, HashSet<string> processedLanguages = null)
            {
                if (processedLanguages == null)
                {
                    processedLanguages = new HashSet<string>();
                }

                var firstTierLanguages = _languages.Where(pair => pair.Value.Name.ToLowerInvariant() == languageName && processedLanguages.Add(pair.Key))
                    .Select(pair => pair.Key)
                    .ToList();

                var otherTiersLayers = firstTierLanguages
                    .SelectMany(s => CalculateDependentLanguages(s, processedLanguages))
                    .ToList();

                return firstTierLanguages.Concat(otherTiersLayers);
            }
        }
    }
}