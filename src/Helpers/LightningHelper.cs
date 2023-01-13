/*
 * NodeGuard
 * Copyright (C) 2023  Elenpay
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program.  If not, see http://www.gnu.org/licenses/.
 *
 */

﻿using AutoMapper;
using FundsManager.Data.Models;
using FundsManager.Services;
using Google.Protobuf;
using NBitcoin;
using NBXplorer;
using NBXplorer.Models;
using Key = FundsManager.Data.Models.Key;

namespace FundsManager.Helpers
{
    public static class LightningHelper
    {
        /// <summary>
        /// Removed duplicated UTXOS from confirmed and unconfirmed changes
        /// </summary>
        /// <param name="utxoChanges"></param>
        public static void RemoveDuplicateUTXOs(this UTXOChanges utxoChanges)
        {
            utxoChanges.Confirmed.UTXOs = utxoChanges.Confirmed.UTXOs.DistinctBy(x => x.Outpoint).ToList();
            utxoChanges.Unconfirmed.UTXOs = utxoChanges.Unconfirmed.UTXOs.DistinctBy(x => x.Outpoint).ToList();
        }
        
        /// <summary>
        /// Helper that adds global xpubs fields and derivation paths in the PSBT inputs to allow hardware wallets or the remote signer to find the right key to sign
        /// </summary>
        /// <param name="logger"></param>
        /// <param name="keys"></param>
        /// <param name="result"></param>
        /// <param name="selectedUtxOs"></param>
        /// <param name="multisigCoins"></param>
        /// <exception cref="ArgumentException"></exception>
        public static (PSBT?, bool) AddDerivationData(ILogger logger, IEnumerable<Key> keys , (PSBT?, bool) result, List<UTXO> selectedUtxOs,
            List<ScriptCoin> multisigCoins)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (keys == null) throw new ArgumentNullException(nameof(keys));
            if (selectedUtxOs == null) throw new ArgumentNullException(nameof(selectedUtxOs));
            if (multisigCoins == null) throw new ArgumentNullException(nameof(multisigCoins));
            
            var nbXplorerNetwork = CurrentNetworkHelper.GetCurrentNetwork();
            foreach (var key in keys)
            {
                if (string.IsNullOrWhiteSpace(key.MasterFingerprint) || string.IsNullOrWhiteSpace(key.XPUB)) continue;
                
                var bitcoinExtPubKey = new BitcoinExtPubKey(key.XPUB, nbXplorerNetwork);
                var masterFingerprint = HDFingerprint.Parse(key.MasterFingerprint);
                var rootedKeyPath = new RootedKeyPath(masterFingerprint, new KeyPath(key.Path));

                //Global xpubs field addition
                result.Item1.GlobalXPubs.Add(
                    bitcoinExtPubKey,
                    rootedKeyPath
                );

                foreach (var selectedUtxo in selectedUtxOs)
                {
                    var utxoDerivationPath = KeyPath.Parse(key.Path).Derive(selectedUtxo.KeyPath);
                    var derivedPubKey = bitcoinExtPubKey.Derive(selectedUtxo.KeyPath).GetPublicKey();

                    var input = result.Item1.Inputs.FirstOrDefault(input =>
                        input?.GetCoin()?.Outpoint == selectedUtxo.Outpoint);
                    var addressRootedKeyPath = new RootedKeyPath(masterFingerprint, utxoDerivationPath);
                    var multisigCoin = multisigCoins.FirstOrDefault(x => x.Outpoint == selectedUtxo.Outpoint);

                    if (multisigCoin != null && input != null && multisigCoin.Redeem.GetAllPubKeys().Contains(derivedPubKey))
                    {
                        input.AddKeyPath(derivedPubKey, addressRootedKeyPath);
                    }
                    else
                    {
                        var errorMessage = $"Invalid derived pub key for utxo:{selectedUtxo.Outpoint}";
                        logger.LogError(errorMessage);
                        throw new ArgumentException(errorMessage, nameof(derivedPubKey));
                    }
                }
            }

            return result;
        }


        /// <summary>
        /// Generates the ExplorerClient for using nbxplorer based on a bitcoin networy type
        /// </summary>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static (Network nbXplorerNetwork, ExplorerClient nbxplorerClient) GenerateNetwork()
        {
            var nbxplorerUri = Environment.GetEnvironmentVariable("NBXPLORER_URI") ??
                               throw new ArgumentNullException("Environment.GetEnvironmentVariable(\"NBXPLORER_URI\")");

            //Nbxplorer api client

            var nbXplorerNetwork = CurrentNetworkHelper.GetCurrentNetwork();

            var provider = new NBXplorerNetworkProvider(nbXplorerNetwork.ChainName);
            var nbxplorerClient = new ExplorerClient(provider.GetFromCryptoCode(nbXplorerNetwork.NetworkSet.CryptoCode),
                new Uri(nbxplorerUri));
            return (nbXplorerNetwork, nbxplorerClient);
        }

        /// <summary>
        /// Helper to select coins from a wallet for requests (Withdrawals, ChannelOperationRequest). FIFO is the coin selection
        /// </summary>
        /// <param name="wallet"></param>
        /// <param name="satsAmount"></param>
        /// <param name="utxoChanges"></param>
        /// <param name="lockedUTXOs"></param>
        /// <param name="logger"></param>
        /// <param name="mapper"></param>
        /// <returns></returns>
        public static async Task<(List<ScriptCoin> coins, List<UTXO> selectedUTXOs)> SelectCoins(
           Wallet wallet, long satsAmount, UTXOChanges utxoChanges, List<FMUTXO> lockedUTXOs, ILogger logger, IMapper mapper)
        {
            if (wallet == null) throw new ArgumentNullException(nameof(wallet));
            if (utxoChanges == null) throw new ArgumentNullException(nameof(utxoChanges));
            if (lockedUTXOs == null) throw new ArgumentNullException(nameof(lockedUTXOs));
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            if (mapper == null) throw new ArgumentNullException(nameof(mapper));
            if (wallet == null) throw new ArgumentNullException(nameof(wallet));
            if (satsAmount <= 0) throw new ArgumentOutOfRangeException(nameof(satsAmount));

            var derivationStrategy = wallet.GetDerivationStrategy();
            var selectedUTXOs = new List<UTXO>();
            var coins = new List<ScriptCoin>();

            var availableUTXOs = new List<UTXO>();
            foreach (var utxo in utxoChanges.Confirmed.UTXOs)
            {
                var fmUtxo = mapper.Map<UTXO, FMUTXO>(utxo);

                if (lockedUTXOs.Contains(fmUtxo))
                {
                    logger.LogInformation("Removing UTXO: {Utxo} from UTXO set as it is locked", fmUtxo.ToString());
                }
                else
                {
                    availableUTXOs.Add(utxo);
                }
            }

            if (!availableUTXOs.Any())
            {
                logger.LogError("The PSBT cannot be generated, no UTXOs are available for walletId: {WalletId}",
                    wallet.Id);
                return (coins, selectedUTXOs);
            }

            var utxosStack = new Stack<UTXO>(availableUTXOs.OrderByDescending(x => x.Confirmations));

            //FIFO Algorithm to match the amount, oldest UTXOs are first taken

            var totalUTXOsConfirmedSats = utxosStack.Sum(x => ((Money)x.Value).Satoshi);

            if (totalUTXOsConfirmedSats < satsAmount)
            {
                logger.LogError(
                    "Error, the total UTXOs set balance for walletid: {WalletId} ({AvailableSats} sats) is less than the amount in the request ({RequestedSats} sats)",
                    wallet.Id, totalUTXOsConfirmedSats, satsAmount);
                return (coins, selectedUTXOs);
            }

            var utxosSatsAmountAccumulator = 0M;

            var iterations = 0;
            while (satsAmount >= utxosSatsAmountAccumulator)
            {
                if (utxosStack.TryPop(out var utxo))
                {
                    selectedUTXOs.Add(utxo);
                    utxosSatsAmountAccumulator += ((Money)utxo.Value).Satoshi;
                }

                iterations++;

                if (iterations == 1_000)
                {
                    break;
                }
            }

            //UTXOS to Enumerable of ICOINS

            coins = selectedUTXOs.Select(x => x.AsCoin(derivationStrategy).ToScriptCoin(x.ScriptPubKey))
                .ToList();

            return (coins, selectedUTXOs);
        }

        /// <summary>
        /// Returns the fee rate (sat/vb) for a tx, 4 is the value in regtest
        /// </summary>
        /// <param name="nbXplorerNetwork"></param>
        /// <param name="nbxplorerClient"></param>
        /// <returns></returns>
        public static async Task<GetFeeRateResult> GetFeeRateResult(Network nbXplorerNetwork,
            ExplorerClient nbxplorerClient)
        {
            GetFeeRateResult feeRateResult;
            if (nbXplorerNetwork == Network.RegTest)
            {
                feeRateResult = new GetFeeRateResult
                {
                    BlockCount = 1,
                    FeeRate = new FeeRate(4M)
                };
            }
            else
            {
                //TODO Maybe the block confirmation count can be a parameter.
                feeRateResult = await nbxplorerClient.GetFeeRateAsync(1); //To be confirmed in 1 block
            }

            return feeRateResult;
        }

        /// <summary>
        /// Combines a list of PSBTs strings
        /// </summary>
        /// <param name="signedPsbts"></param>
        /// <param name="combinedPSBT"></param>
        /// <returns>A combined PSBT or null if error.</returns>
        public static PSBT? CombinePSBTs(IEnumerable<string> signedPsbts, ILogger logger)
        {
            PSBT? combinedPSBT = null;
            try
            {
                foreach (var signedPSBT in signedPsbts)
                {
                    if (PSBT.TryParse(signedPSBT, CurrentNetworkHelper.GetCurrentNetwork(), out var parsedPSBT))
                    {
                        combinedPSBT = combinedPSBT == null ? parsedPSBT : combinedPSBT.Combine(parsedPSBT);
                    }
                }
            }
            catch (Exception e)
            {
                logger.LogError(e, "Error while combining PSBTs");
                combinedPSBT = null;
            }

            return combinedPSBT;
        }

        /// <summary>
        /// Helper for decoding bytestring-based LND representation of TxIds
        /// </summary>
        /// <param name="TxIdBytes"></param>
        /// <returns></returns>
        public static string DecodeTxId(ByteString TxIdBytes)
        {
            return Convert.ToHexString(TxIdBytes
                    .ToByteArray()
                    .Reverse() //Endianness of the txidbytes is different we need to reverse
                    .ToArray())
                .ToLower();
        }
    }
}