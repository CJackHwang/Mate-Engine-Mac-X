using System;
using Unity.Pipeline.Config;
using Unity.Pipeline.Models;
using UnityEngine;

namespace Unity.Pipeline
{
    /// <summary>The Player-side <see cref="BasePipelineServer"/>, owned by a <see cref="RuntimePipelineDriver"/>.</summary>
    public class RuntimePipelineServer : BasePipelineServer
    {
        private RuntimePipelineConfig m_Config;
        private RuntimeInstanceDescriptor m_InstanceDescriptor;

        /// <summary>
        /// Guards mutate-then-write of m_InstanceDescriptor: concurrent /api/status probes both
        /// reach UpdateHeartBeat now, and RuntimeInstanceDescriptor.WriteToWorkingDirectory's own
        /// lock only covers the write, not the field mutation feeding it.
        /// </summary>
        private readonly object m_HeartbeatLock = new object();

        /// <summary>UTC time this server instance started listening.</summary>
        public override DateTime StartedAt => m_InstanceDescriptor == null ? new DateTime() : m_InstanceDescriptor.StartedAt;

        /// <summary>Create a server bound to the given configuration.</summary>
        /// <param name="config">The runtime pipeline configuration.</param>
        public RuntimePipelineServer(RuntimePipelineConfig config)
        {
            m_Config = config;
        }

        /// <summary>Runtime servers use ports 7900-7949 (test runtime servers use 7950-7999).</summary>
        /// <returns>The inclusive port range to try when binding the listener.</returns>
        protected override (int basePort, int maxPort) GetPortRange()
        {
            return (7900, 7949); // Runtime production (test runtime servers use 7950-7999)
        }

        /// <summary>Write the runtime instance descriptor to the working directory for CLI discovery.</summary>
        protected override void CreateInstanceDescriptor()
        {
            // Create and write instance descriptor for CLI discovery
            m_InstanceDescriptor = RuntimeInstanceDescriptor.CreateCurrent(Port, m_Config);
            RuntimeInstanceDescriptor.WriteToWorkingDirectory(m_InstanceDescriptor);
        }

        /// <summary>Remove the runtime instance descriptor on shutdown.</summary>
        protected override void DeleteInstanceDescriptor()
        {
            // Clean up instance descriptor file
            if (m_InstanceDescriptor != null)
            {
                RuntimeInstanceDescriptor.RemoveFromWorkingDirectory();
            }
            m_InstanceDescriptor = null;
        }

        /// <summary>Refresh the descriptor's heartbeat timestamp.</summary>
        protected override void UpdateHeartBeat()
        {
            // Update heartbeat in instance descriptor
            if (m_InstanceDescriptor == null)
                return;

            lock (m_HeartbeatLock)
            {
                m_InstanceDescriptor.LastHeartbeat = DateTime.UtcNow;
                try
                {
                    RuntimeInstanceDescriptor.WriteToWorkingDirectory(m_InstanceDescriptor);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Failed to update instance descriptor: {ex.Message}");
                }
            }
        }

        /// <summary>Build the Player-specific status/heartbeat payload for /api/status.</summary>
        /// <returns>The status payload.</returns>
        protected override object GetServerStatus()
        {
            UpdateHeartBeat();
            return new
            {
                status = m_InstanceDescriptor == null ? "error" : "ready",
                lastHeartbeat = m_InstanceDescriptor?.LastHeartbeat,
                capabilities = Capabilities
            };
        }

        /// <summary>The bearer token clients must present to authenticate requests.</summary>
        /// <returns>The current token.</returns>
        protected override string GetToken()
        {
            return m_InstanceDescriptor.EvalToken;
        }
    }
}
