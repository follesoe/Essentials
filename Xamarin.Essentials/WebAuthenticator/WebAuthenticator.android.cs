﻿using System;
using System.Threading.Tasks;
using Android.Content;
using Android.Support.CustomTabs;

namespace Xamarin.Essentials
{
    public partial class WebAuthenticator
    {
        static TaskCompletionSource<AuthResult> tcsResponse = null;

        static Uri uri = null;

        static CustomTabsActivityManager CustomTabsActivityManager { get; set; }

        static Uri RedirectUri { get; set; }

        internal static Task<AuthResult> ResponseTask
            => tcsResponse?.Task;

        internal static bool OnResume(Intent intent)
        {
            // If we aren't waiting on a task, don't handle the url
            if (tcsResponse?.Task?.IsCompleted ?? true)
                return false;

            if (intent == null)
            {
                tcsResponse.TrySetCanceled();
                return false;
            }

            try
            {
                var intentUri = new Uri(intent.Data.ToString());

                // Only handle schemes we expect
                if (!WebUtils.CanHandleCallback(RedirectUri, intentUri))
                {
                    tcsResponse.TrySetException(new InvalidOperationException("Invalid Redirect URI"));
                    return false;
                }

                tcsResponse?.TrySetResult(new AuthResult(intentUri));
                return true;
            }
            catch (Exception ex)
            {
                tcsResponse.TrySetException(ex);
                return false;
            }
        }

        static Task<AuthResult> PlatformAuthenticateAsync(Uri url, Uri callbackUrl)
        {
            // TODO: Check for intent filter registered for scheme
            // We can query package manager to get intents that can handle our callbackurl scheme
            // to ensure we actually set this up correctly

            // Cancel any previous task that's still pending
            if (tcsResponse?.Task != null && !tcsResponse.Task.IsCompleted)
                tcsResponse.TrySetCanceled();

            tcsResponse = new TaskCompletionSource<AuthResult>();
            tcsResponse.Task.ContinueWith(t =>
            {
                // Cleanup when done
                if (CustomTabsActivityManager != null)
                {
                    CustomTabsActivityManager.NavigationEvent -= CustomTabsActivityManager_NavigationEvent;
                    CustomTabsActivityManager.CustomTabsServiceConnected -= CustomTabsActivityManager_CustomTabsServiceConnected;

                    try
                    {
                        CustomTabsActivityManager?.Client?.Dispose();
                    }
                    finally
                    {
                        CustomTabsActivityManager = null;
                    }
                }
            });

            uri = url;
            RedirectUri = callbackUrl;

            CustomTabsActivityManager = CustomTabsActivityManager.From(Platform.GetCurrentActivity(true));
            CustomTabsActivityManager.NavigationEvent += CustomTabsActivityManager_NavigationEvent;
            CustomTabsActivityManager.CustomTabsServiceConnected += CustomTabsActivityManager_CustomTabsServiceConnected;

            if (!CustomTabsActivityManager.BindService())
            {
                // Fall back to opening the system browser if necessary
                var browserIntent = new Intent(Intent.ActionView, global::Android.Net.Uri.Parse(url.OriginalString));
                Platform.CurrentActivity.StartActivity(browserIntent);
            }

            return WebAuthenticator.ResponseTask;
        }

        static void CustomTabsActivityManager_CustomTabsServiceConnected(ComponentName name, CustomTabsClient client)
        {
            var builder = new CustomTabsIntent.Builder(CustomTabsActivityManager.Session)
                                                  .SetShowTitle(true);

            var customTabsIntent = builder.Build();
            customTabsIntent.Intent.AddFlags(ActivityFlags.SingleTop | ActivityFlags.NoHistory | ActivityFlags.NewTask);

            var ctx = Platform.CurrentActivity;

            CustomTabsHelper.AddKeepAliveExtra(ctx, customTabsIntent.Intent);

            customTabsIntent.LaunchUrl(ctx, global::Android.Net.Uri.Parse(uri.OriginalString));
        }

        static void CustomTabsActivityManager_NavigationEvent(int navigationEvent, global::Android.OS.Bundle extras) =>
            System.Diagnostics.Debug.WriteLine($"CustomTabs.NavigationEvent: {navigationEvent}");
    }
}