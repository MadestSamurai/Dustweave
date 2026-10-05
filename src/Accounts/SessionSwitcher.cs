namespace BD2AccountSessionManager;

internal static class SessionSwitcher
{
    internal static ActivationResult Activate(
        SessionVault vault,
        SessionSlot target,
        bool allowIncompleteCurrent = false)
    {
        SessionRegistry.ValidateSlot(target);
        SessionStatus status = SessionRegistry.GetStatus();
        if (status.GameRunning || status.StarterRunning)
        {
            throw new SessionManagerException("游戏或启动器仍在运行，已拒绝切换。请直接关闭游戏并退出启动器；不要在游戏内注销账户。");
        }

        if (SessionRegistry.HasPendingLauncherToken())
        {
            throw new SessionManagerException("检测到尚未被游戏消费的启动器登录令牌，已拒绝切换。请先完成该次启动并直接关闭游戏。");
        }

        if (!status.Complete)
        {
            if (!allowIncompleteCurrent)
            {
                throw new SessionManagerException("当前活动会话不完整。若这是 prepare-login 的结果，请运行 recover 恢复切换前会话。");
            }

            SessionRegistry.WriteAndVerify(target);
            return new ActivationResult(
                target.Alias,
                vault.Fingerprint(target),
                Changed: true,
                RecoveryCapturedAtUtc: null);
        }

        SessionSlot current = SessionRegistry.Capture("automatic-recovery");
        if (SessionRegistry.SessionsEqual(current, target))
        {
            return new ActivationResult(
                target.Alias,
                vault.Fingerprint(target),
                Changed: false,
                RecoveryCapturedAtUtc: null);
        }

        SlotSummary recovery = vault.SaveRecovery(current);
        try
        {
            SessionStatus beforeWrite = SessionRegistry.GetStatus();
            if (beforeWrite.GameRunning
                || beforeWrite.StarterRunning
                || SessionRegistry.HasPendingLauncherToken())
            {
                throw new SessionManagerException("建立恢复点后检测到游戏、启动器或待消费令牌，已在写入前安全停止切换。");
            }

            SessionRegistry.WriteAndVerify(target);
        }
        catch (Exception activationError)
        {
            try
            {
                SessionRegistry.WriteAndVerify(current);
            }
            catch
            {
                throw new SessionManagerException(
                    "切换失败且自动回滚未能完成。请不要启动游戏，使用 recover 命令读取已验证的自动恢复副本。");
            }

            throw new SessionManagerException(
                $"切换失败，原会话已自动回滚。错误类型：{activationError.GetType().Name}。");
        }

        return new ActivationResult(
            target.Alias,
            vault.Fingerprint(target),
            Changed: true,
            RecoveryCapturedAtUtc: recovery.CapturedAtUtc);
    }

    internal static SlotSummary PrepareLogin(SessionVault vault, SessionSlot preservedSlot)
    {
        SessionRegistry.ValidateSlot(preservedSlot);
        SessionStatus status = SessionRegistry.GetStatus();
        if (status.GameRunning || status.StarterRunning)
        {
            throw new SessionManagerException("游戏或启动器仍在运行，已拒绝准备新登录。请直接关闭游戏并退出启动器。");
        }

        if (!status.Complete)
        {
            throw new SessionManagerException("当前活动会话不完整，无法确认第一账户已经安全保存。");
        }

        if (SessionRegistry.HasPendingLauncherToken())
        {
            throw new SessionManagerException("检测到尚未被游戏消费的启动器登录令牌，已拒绝准备新登录。");
        }

        SessionSlot current = SessionRegistry.Capture("automatic-recovery");
        if (!SessionRegistry.SessionsEqual(current, preservedSlot))
        {
            throw new SessionManagerException("当前活动会话与指定的已保存槽位不一致，已拒绝移除本机会话。");
        }

        SlotSummary recovery = vault.SaveRecovery(current);
        try
        {
            SessionRegistry.ClearAndVerify();
        }
        catch (Exception clearError)
        {
            try
            {
                SessionRegistry.WriteAndVerify(current);
            }
            catch
            {
                throw new SessionManagerException(
                    "移除本机会话失败且自动回滚未完成。请不要启动游戏，运行 recover 读取自动恢复副本。");
            }

            throw new SessionManagerException(
                $"移除本机会话失败，原会话已自动回滚。错误类型：{clearError.GetType().Name}。");
        }

        return recovery;
    }
}
