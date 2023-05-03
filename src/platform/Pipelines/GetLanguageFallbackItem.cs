using System.Collections.Generic;
using System.Linq;
using System.Web;
using Sitecore.Data;
using Sitecore.Data.Items;
using Sitecore.Data.LanguageFallback;
using Sitecore.Data.Managers;
using Sitecore.Diagnostics;
using Sitecore.Globalization;
using Sitecore.Pipelines.ItemProvider.GetItem;
using Version = System.Version;

namespace Sitecore.Demo.Edge.Website.Pipelines
{
    public class GetLanguageFallbackItem: GetItemProcessor
    {
        /// <summary>
        ///     Processes the specified args.
        /// </summary>
        /// <param name="args">The args.</param>
        public override void Process(GetItemArgs args)
        {
            if (args.Language == null || args.Language.Name=="")
            {
                Log.Warn($"-------------{args.ItemId} #25", this); return;
            }
            var switcherValue = LanguageFallbackItemSwitcher.CurrentValue;
            if ((switcherValue == false || !args.AllowLanguageFallback) && switcherValue != true)
            {
                Log.Warn($"-------------{args.ItemId} #30: switcherValue={switcherValue} args.AllowLanguageFallback={args.AllowLanguageFallback}", this);
                return;
            }

            if (args.Result == null && args.Handled)
            {
                return;
            }

            var item = args.Result ?? ((object)args.ItemId != null
                ? args.FallbackProvider.GetItem(args.ItemId, args.Language, args.Version, args.Database, args.SecurityCheck)
                : args.FallbackProvider.GetItem(args.ItemPath, args.Language, args.Version, args.Database, args.SecurityCheck));

            args.Result = item;

            if (item == null || !item.LanguageFallbackEnabled)
            {
                Log.Warn($"-------------{args.ItemId} #47: item={item}", this);
                return;
            }

            // it's necessary to track cyclic fallback
            var usedLanguages = new List<Language>(4);
            var fallbackItem = item;
            var fallbackLanguage = args.Language;
            while (fallbackItem != null && (!fallbackItem.Name.StartsWith("__") || StandardValuesManager.IsStandardValuesHolder(fallbackItem)) && fallbackItem.RuntimeSettings.TemporaryVersion)
            {
                Log.Warn($"-------------{args.ItemId} #57: fallbackItem.ID={fallbackItem.ID} fallbackItem.Name={fallbackItem.Name} StandardValuesManager.IsStandardValuesHolder(fallbackItem)={StandardValuesManager.IsStandardValuesHolder(fallbackItem)} " +
                    $"fallbackItem.RuntimeSettings.TemporaryVersion={fallbackItem.RuntimeSettings.TemporaryVersion}", this);
                usedLanguages.Add(fallbackLanguage);

                fallbackLanguage = LanguageFallbackManager.GetFallbackLanguage(fallbackLanguage, args.Database);

                Log.Warn($"-------------{args.ItemId} #63: fallbackLanguage={LanguageFallbackManager.GetFallbackLanguage(fallbackLanguage, args.Database)}", this);
                if (fallbackLanguage == null || usedLanguages.Contains(fallbackLanguage))
                {
                    Log.Warn($"-------------{args.ItemId} #66", this);
                    return;
                }

                fallbackItem = args.FallbackProvider.GetItem(fallbackItem.ID, fallbackLanguage, Sitecore.Data.Version.Latest, args.Database, args.SecurityCheck);
                Log.Warn($"-------------{args.ItemId} #71: fallbackItem.ID={fallbackItem.ID} fallbackItem.Name={fallbackItem.Name} StandardValuesManager.IsStandardValuesHolder(fallbackItem)={StandardValuesManager.IsStandardValuesHolder(fallbackItem)} " +
                    $"fallbackItem.RuntimeSettings.TemporaryVersion={fallbackItem.RuntimeSettings.TemporaryVersion}", this);
            }

            if (fallbackItem == null || fallbackLanguage == args.Language)
            {
                Log.Warn($"-------------{args.ItemId} #77: fallbackLanguage={fallbackLanguage} args.Language={args.Language}", this);
                return;
            }

            // TODO: fallbackItem.InnerData.Fields does not include field values from __standard values
            var stubData = new ItemData(fallbackItem.InnerData.Definition, item.Language, item.Version, fallbackItem.InnerData.Fields);
            var stubItem = new Item(item.ID, stubData, item.Database)
            {
                OriginalLanguage = fallbackItem.Language
            };

            Log.Warn($"-------------{args.ItemId} #88: stubItem.OriginalLanguage={stubItem.OriginalLanguage}", this);

            args.Result = stubItem;
        }
    }
}