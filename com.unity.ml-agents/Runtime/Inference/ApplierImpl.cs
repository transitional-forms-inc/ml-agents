using System;
using System.Collections.Generic;
using TransformsAI.MicroMLAgents.Actuators;
using TransformsAI.MicroMLAgents.Inference.Utils;
using Unity.Barracuda;
using UnityEngine;

namespace TransformsAI.MicroMLAgents.Inference
{
    /// <summary>
    /// The Applier for the Continuous Action output tensor. Tensor is assumed to contain the
    /// continuous action data of the agents in the batch.
    /// </summary>
    internal class ContinuousActionOutputApplier : TensorApplier.IApplier
    {
        readonly ActionSpec m_ActionSpec;

        public ContinuousActionOutputApplier(ActionSpec actionSpec)
        {
            m_ActionSpec = actionSpec;
        }

        public void Apply(TensorProxy tensorProxy, IList<IAgent> batch)
        {
            var actionSize = tensorProxy.shape[^1];
            var agentIndex = 0;
            for (var i = 0; i < batch.Count; i++)
            {
                var agent = batch[i];
                var actionBuffer = agent.ActionBuffer;
                if (actionBuffer.IsEmpty())
                {
                    actionBuffer = new ActionBuffers(m_ActionSpec);
                    agent.ActionBuffer = actionBuffer;
                }
                var continuousBuffer = actionBuffer.ContinuousActions;
                for (var j = 0; j < actionSize; j++)
                {
                    continuousBuffer[j] = tensorProxy.data[agentIndex, j];
                }
            }
        }
    }

    /// <summary>
    /// The Applier for the Discrete Action output tensor.
    /// </summary>
    internal class DiscreteActionOutputApplier : TensorApplier.IApplier
    {
        readonly ActionSpec m_ActionSpec;


        public DiscreteActionOutputApplier(ActionSpec actionSpec, int seed, ITensorAllocator allocator)
        {
            m_ActionSpec = actionSpec;
        }

        public void Apply(TensorProxy tensorProxy, IList<IAgent> batch)
        {
            var actionSize = tensorProxy.shape[^1];
            for (var agentIndex = 0; agentIndex < batch.Count; agentIndex++)
            {
                var agent = batch[agentIndex];
                var actionBuffer = agent.ActionBuffer;
                if (actionBuffer.IsEmpty())
                {
                    actionBuffer = new ActionBuffers(m_ActionSpec);
                    agent.ActionBuffer = actionBuffer;
                }
                var discreteBuffer = actionBuffer.DiscreteActions;
                for (var j = 0; j < actionSize; j++)
                {
                    discreteBuffer[j] = (int)tensorProxy.data[agentIndex, j];
                }
            }
        }
    }


    /// <summary>
    /// The Applier for the Discrete Action output tensor. Uses multinomial to sample discrete
    /// actions from the logits contained in the tensor.
    /// </summary>
    internal class LegacyDiscreteActionOutputApplier : TensorApplier.IApplier
    {
        readonly int[] m_ActionSize;
        readonly Multinomial m_Multinomial;
        readonly ActionSpec m_ActionSpec;
        readonly int[] m_StartActionIndices;
        readonly float[] m_CdfBuffer;


        public LegacyDiscreteActionOutputApplier(ActionSpec actionSpec, int seed, ITensorAllocator allocator)
        {
            m_ActionSize = actionSpec.BranchSizes;
            m_Multinomial = new Multinomial(seed);
            m_ActionSpec = actionSpec;
            m_StartActionIndices = Utilities.CumSum(m_ActionSize);

            // Scratch space for computing the cumulative distribution function.
            // In order to reuse it, make it the size of the largest branch.
            var largestBranch = Mathf.Max(m_ActionSize);
            m_CdfBuffer = new float[largestBranch];
        }

        public void Apply(TensorProxy tensorProxy, IList<int> actionIds, Dictionary<int, ActionBuffers> lastActions)
        {
            var agentIndex = 0;
            for (var i = 0; i < actionIds.Count; i++)
            {
                var agentId = actionIds[i];
                if (lastActions.ContainsKey(agentId))
                {
                    var actionBuffer = lastActions[agentId];
                    if (actionBuffer.IsEmpty())
                    {
                        actionBuffer = new ActionBuffers(m_ActionSpec);
                        lastActions[agentId] = actionBuffer;
                    }
                    var discreteBuffer = actionBuffer.DiscreteActions;
                    for (var j = 0; j < m_ActionSize.Length; j++)
                    {
                        ComputeCdf(tensorProxy, agentIndex, m_StartActionIndices[j], m_ActionSize[j]);
                        discreteBuffer[j] = m_Multinomial.Sample(m_CdfBuffer, m_ActionSize[j]);
                    }
                }
                agentIndex++;
            }
        }

        /// <summary>
        /// Compute the cumulative distribution function for a given agent's action
        /// given the log-probabilities.
        /// The results are stored in m_CdfBuffer, which is the size of the largest action's number of branches.
        /// </summary>
        /// <param name="logProbs"></param>
        /// <param name="batch">Index of the agent being considered</param>
        /// <param name="channelOffset">Offset into the tensor's channel.</param>
        /// <param name="branchSize"></param>
        internal void ComputeCdf(TensorProxy logProbs, int batch, int channelOffset, int branchSize)
        {
            // Find the class maximum
            var maxProb = float.NegativeInfinity;
            for (var cls = 0; cls < branchSize; ++cls)
            {
                maxProb = Mathf.Max(logProbs.data[batch, cls + channelOffset], maxProb);
            }

            // Sum the log probabilities and compute CDF
            var sumProb = 0.0f;
            for (var cls = 0; cls < branchSize; ++cls)
            {
                sumProb += Mathf.Exp(logProbs.data[batch, cls + channelOffset] - maxProb);
                m_CdfBuffer[cls] = sumProb;
            }
        }

        public void Apply(TensorProxy tensorProxy, IList<IAgent> infos)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// The Applier for the Memory output tensor. Tensor is assumed to contain the new
    /// memory data of the agents in the batch.
    /// </summary>
    internal class MemoryOutputApplier : TensorApplier.IApplier
    {
        public void Apply(TensorProxy tensorProxy, IList<IAgent> infos)
        {
            var memorySize = tensorProxy.data.width;
            for (var agentIndex = 0; agentIndex < infos.Count; agentIndex++)
            {
                var agent = infos[agentIndex];
                var memory = agent.Memory;

                if (memory == null)
                {
                    if (memorySize == 0) continue;
                    throw new UnityAgentsException("Agent With Memory has no memory initialized");
                }

                var existingSize = memory.Count;

                if (existingSize != memorySize)
                {
                    memory.Clear();
                    for (var j = 0; j < memorySize; j++) memory.Add(0f);
                }

                for (var j = 0; j < memorySize; j++)
                {
                    memory[j] = tensorProxy.data[agentIndex, 0, j, 0];
                }
                
            }
        }
    }
}
