// Copyright (c) 2026 Rinko
// SPDX-License-Identifier: GPL-3.0-only
using System;

namespace CodexProxyManager
{
    internal static class PrivacyProtection
    {
        internal static bool WebRtcRequested(ManagedApplication app)
        {
            return app != null && app.LaunchMode == "chromium" && app.LimitWebRtc != false;
        }

        internal static string Arguments(ManagedApplication app)
        {
            if (app == null || app.LaunchMode != "chromium") return "";
            string flags = "";
            if (WebRtcRequested(app))
                // Chrome's preference switch and the older/headless switch are different.
                // Electron may ignore both; passing them is a request, not proof of protection.
                flags += " --webrtc-ip-handling-policy=disable_non_proxied_udp" +
                    " --force-webrtc-ip-handling-policy=disable_non_proxied_udp";
            if (app.RestrictBrowserDns)
                flags += " --host-resolver-rules=\"MAP * ~NOTFOUND, EXCLUDE localhost, EXCLUDE 127.0.0.1, EXCLUDE [::1]\"";
            if (app.DisableQuic) flags += " --disable-quic";
            return flags;
        }

        internal static string Describe(ManagedApplication app)
        {
            return Describe(app, false);
        }

        internal static string Describe(ManagedApplication app, bool launched)
        {
            if (app == null) return "没有应用隐私设置；未验证泄漏情况。";
            if (app.LaunchMode != "chromium")
                return "环境变量模式不支持 WebRTC、浏览器 DNS 或 QUIC 限制；未验证泄漏情况。";
            string requested = WebRtcRequested(app) ? "WebRTC 直连限制" : "";
            if (app.RestrictBrowserDns) requested += (requested.Length == 0 ? "" : "、") + "浏览器 DNS 限制";
            if (app.DisableQuic) requested += (requested.Length == 0 ? "" : "、") + "关闭 QUIC";
            return requested.Length == 0 ? "隐私启动限制未启用；未验证泄漏情况。" :
                (launched ? "本次启动已请求：" : "已配置，下次从管理器启动时请求：") + requested +
                "。效果待验证；Electron/自定义内核可能忽略参数。";
        }
    }
}
