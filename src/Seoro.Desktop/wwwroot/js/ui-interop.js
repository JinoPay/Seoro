// Seoro UI Kit JS interop — 팝오버 fixed 포지셔닝 + 모달 포커스
window.seoroUi = {
    // overflow 컨테이너 내부 앵커용: 뷰포트 기준 fixed 좌표 계산 + 가장자리 플리핑
    positionPopover(anchor, panel, placement) {
        if (!anchor || !panel) return;
        const a = anchor.getBoundingClientRect();
        const p = panel.getBoundingClientRect();
        const vw = window.innerWidth;
        const vh = window.innerHeight;
        const gap = 4;

        let top = placement.startsWith('Top') ? a.top - p.height - gap : a.bottom + gap;
        let left = placement.endsWith('End') ? a.right - p.width : a.left;

        // 뷰포트 플리핑/클램핑
        if (top + p.height > vh && a.top - p.height - gap >= 0) top = a.top - p.height - gap;
        if (top < 0) top = gap;
        if (left + p.width > vw) left = vw - p.width - gap;
        if (left < 0) left = gap;

        panel.style.position = 'fixed';
        panel.style.top = top + 'px';
        panel.style.left = left + 'px';
        panel.style.right = 'auto';
        panel.style.bottom = 'auto';
    },

    // 채팅 입력 textarea 자동 높이 (Mud InputSizing.Auto 대체, 최대 높이는 CSS max-height)
    autoGrowChatInput(containerId) {
        const container = document.getElementById(containerId);
        if (!container) return;
        const el = container.querySelector('textarea');
        if (!el) return;
        el.style.height = 'auto';
        el.style.height = el.scrollHeight + 'px';
    },

    focusLastModal() {
        const modals = document.querySelectorAll('.ui-modal');
        if (modals.length === 0) return;
        const last = modals[modals.length - 1];
        const focusable = last.querySelector('input, textarea, select, button:not(.ui-iconbtn)');
        (focusable || last).focus();
    },
};
