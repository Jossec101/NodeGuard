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

using AutoFixture;
using AutoMapper;
using Microsoft.Extensions.Logging;
using FundsManager.Data;
using FundsManager.Data.Models;
using FundsManager.Data.Repositories.Interfaces;
using FundsManager.TestHelpers;
using NBXplorer;
using NBXplorer.Models;
using FluentAssertions;
using FundsManager.Helpers;
using Google.Protobuf;
using Lnrpc;
using NBitcoin;
using NBXplorer.DerivationStrategy;
using Grpc.Core;
using Microsoft.EntityFrameworkCore;
using Unmockable.Exceptions;

namespace FundsManager.Services
{
    public class LightningServiceTests
    {
        ILogger<LightningService> logger = new Mock<ILogger<LightningService>>().Object;
    
        [Fact]
        public void CheckArgumentsAreValid_ArgumentNull()
        {
            // Act
            var act = () => LightningService.CheckArgumentsAreValid(null, OperationRequestType.Open);
            
            // Assert
            act.Should()
                .Throw<ArgumentNullException>()
                .WithMessage("Value cannot be null. (Parameter 'channelOperationRequest')");
        }

        [Fact]
        public void CheckArgumentsAreValid_RequestTypeNotOpen()
        {
            // Arrange
            var operationRequest = new ChannelOperationRequest
            {
                RequestType = OperationRequestType.Close
            };

            // Act
            var act = () => LightningService.CheckArgumentsAreValid(operationRequest, OperationRequestType.Open);
            
            // Assert
            act.Should()
                .Throw<ArgumentOutOfRangeException>()
                .WithMessage("Specified argument was out of the range of valid values. (Parameter 'Invalid request. Requested $Close on $Open method')");
        }

        [Fact]
        public async void OpenChannel_ChannelOperationRequestNotFound()
        {
            // Arrange
            var dbContextFactory = new Mock<IDbContextFactory<ApplicationDbContext>>();
            var channelOperationRequestRepository = new Mock<IChannelOperationRequestRepository>();

            channelOperationRequestRepository
                .Setup(x => x.GetById(It.IsAny<int>()))
                .ReturnsAsync(null as ChannelOperationRequest);

            var lightningService = new LightningService(logger, channelOperationRequestRepository.Object, null, dbContextFactory.Object, null, null, null, null, null, null);

            var operationRequest = new ChannelOperationRequest
            {
                RequestType = OperationRequestType.Open
            };

            // Act
            var act = async () => await lightningService.OpenChannel(operationRequest);
            
            // Assert
            await act.Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage("ChannelOperationRequest not found");
        }

        [Fact]
        public void CheckNodesAreValid_SourceNodeNull()
        {
            // Arrange
            var operationRequest = new ChannelOperationRequest
            {
                RequestType = OperationRequestType.Open,
                DestNode = new Node()
            };

            // Act
            var act = () => LightningService.CheckNodesAreValid(operationRequest);
            
            // Assert
            act.Should()
                .Throw<ArgumentException>()
                .WithMessage("Source or destination null (Parameter 'source')");
        }

        [Fact]
        public void CheckNodesAreValid_DestinationNodeNull()
        {
            // Arrange
            var operationRequest = new ChannelOperationRequest
            {
                RequestType = OperationRequestType.Open,
                SourceNode = new Node()
            };

            // Act
            var act = () => LightningService.CheckNodesAreValid(operationRequest);
            
            // Assert
            act.Should()
                .Throw<ArgumentException>()
                .WithMessage("Source or destination null (Parameter 'source')");
        }
        
        [Fact]
        public void CheckNodesAreValid_MacaroonNotSet()
        {
            // Arrange
            var operationRequest = new ChannelOperationRequest
            {
                RequestType = OperationRequestType.Open,
                SourceNode = new Node(),
                DestNode = new Node()
            };

            // Act
            var act = () => LightningService.CheckNodesAreValid(operationRequest);
            
            // Assert
            act.Should()
                .Throw<UnauthorizedAccessException>()
                .WithMessage("Macaroon not set for source channel");
        }

        [Fact]
        public void CheckNodesAreValid_NodesAreValid()
        {
            // Arrange
            var operationRequest = new ChannelOperationRequest
            {
                RequestType = OperationRequestType.Open,
                SourceNode = new Node()
                {
                    PubKey = "a",
                    ChannelAdminMacaroon = "abc"
                },
                DestNode = new Node()
                {
                    PubKey = "b",
                    ChannelAdminMacaroon = "def"
                }
            };

            // Act
            var result = LightningService.CheckNodesAreValid(operationRequest);
            
            // Assert
            result.Should().NotBeNull();
            result.Item1.Should().NotBeNull();
            result.Item2.Should().NotBeNull();
        }

        [Fact]
        public void CheckNodesAreValid_SourceEqualsDestination()
        {
            // Arrange
            var node = new Node
            {
                PubKey = "A",
                ChannelAdminMacaroon = "abc"
            };
            
            var operationRequest = new ChannelOperationRequest
            {
                RequestType = OperationRequestType.Open,
                SourceNode = node,
                DestNode = node
            };
            
            // Act
            var act = () => LightningService.CheckNodesAreValid(operationRequest);
            
            // Assert
            act.Should()
                .Throw<ArgumentException>()
                .WithMessage("A node cannot open a channel to itself.");
        }

        [Fact]
        public void GetDerivationStrategyBase_NoDerivationScheme()
        {
            // Arrange
            var operationRequest = new ChannelOperationRequest
            {
                Wallet = new Wallet()
            };
            
            // Act
            var act = () => LightningService.GetDerivationStrategyBase(operationRequest);
            
            // Assert
            act.Should()
                .Throw<ArgumentException>()
                .WithMessage("Derivation scheme not found for wallet:0");
        }
   
        [Fact]
        public void GetDerivationStrategyBase_DerivationSchemeExists()
        {
            // Arrange
            var operationRequest = new ChannelOperationRequest
            {
                Wallet = CreateWallet.CreateTestWallet() 
            };
            
            // Act
            var result = LightningService.GetDerivationStrategyBase(operationRequest);
            
            // Assert
            result.Should().NotBeNull();
        }
        
        [Fact]
        public async void GetCloseAddress_NoCloseAddress()
        {
            // Arrange
            var operationRequest = new ChannelOperationRequest
            {
                Wallet = CreateWallet.CreateTestWallet()
            };

            var explorerClient = Interceptor.For<ExplorerClient>()
                .Setup(x => x.GetUnusedAsync(
                    Arg.Ignore<DerivationStrategyBase>(),
                    Arg.Ignore<DerivationFeature>(),
                    Arg.Ignore<int>(),
                    Arg.Ignore<bool>(),
                    Arg.Ignore<CancellationToken>()
                ))
                .Returns(null);
            
            // Act
            var act = async () => await LightningService.GetCloseAddress(operationRequest, operationRequest.Wallet.GetDerivationStrategy()!, explorerClient.As<IUnmockable<ExplorerClient>>());
            
            // Assert
            await act.Should()
                .ThrowAsync<ArgumentException>()
                .WithMessage("Closing address was null for an operation on wallet:0");
            explorerClient.Verify();
        }

        [Fact]
        public async void GetCloseAddress_CloseAddressExists()
        {
            // Arrange
            var operationRequest = new ChannelOperationRequest
            {
                Wallet = CreateWallet.CreateTestWallet()
            };

            var explorerClient = Interceptor.For<ExplorerClient>()
                .Setup(x => x.GetUnusedAsync(
                    Arg.Ignore<DerivationStrategyBase>(),
                    Arg.Ignore<DerivationFeature>(),
                    Arg.Ignore<int>(),
                    Arg.Ignore<bool>(),
                    Arg.Ignore<CancellationToken>()
                ))
                .Returns(new KeyPathInformation());
            
            // Act
            var result = await LightningService.GetCloseAddress(operationRequest,
                operationRequest.Wallet.GetDerivationStrategy()!, explorerClient.As<IUnmockable<ExplorerClient>>());
            
            // Assert
            result.Should().NotBeNull();
            explorerClient.Verify();
        }
        
        [Fact]
        public void GetCombinedPsbt_NoCombinedPSBT()
        {
            // Arrange
            var operationRequest = new ChannelOperationRequest()
            {
                ChannelOperationRequestPsbts = new List<ChannelOperationRequestPSBT>()
            };

            // Act
            var act = () => LightningService.GetCombinedPsbt(operationRequest);
            
            // Assert
            act.Should()
                .Throw<ArgumentException>()
                .WithMessage("Invalid PSBT(null) to be used for the channel op request:0 (Parameter 'combinedPSBT')");
        }
        
        [Fact]
        public void GetCombinedPsbt_CombinedPSBTExists()
        {
            // Arrange
            var channelOpReqPsbts = new List<ChannelOperationRequestPSBT>();
            channelOpReqPsbts.Add(new ChannelOperationRequestPSBT()
            {
                PSBT = "cHNidP8BAF4BAAAAAdz1PWN8JtwrX5q7aREhJbCmD0I/Xn/m84Znzoo0gPXfAQAAAAD/////AeRcqQAAAAAAIgAgmXpf0mpyCEyKLRK/kCrOwYZpkA3QmJHS6iSocRyj7G4AAAAATwEENYfPAy8RJCyAAAAB/DvuQjoBjOttImoGYyiO0Pte4PqdeQqzcNAw4Ecw5sgDgI4uHNSCvdBxlpQ8WoEz0WmvhgIra7A4F3FkTsB0RNcQH8zk3jAAAIABAACAAQAAgE8BBDWHzwNWrAP0gAAAAfkIrkpmsP+hqxS1WvDOSPKnAiXLkBCQLWkBr5C5Po+BAlGvFeBbuLfqwYlbP19H/+/s2DIaAu8iKY+J0KIDffBgEGDzoLMwAACAAQAAgAEAAIBPAQQ1h88DfblGjYAAAAH1InDHaHo6+zUe9PG5owwQ87bTkhcGg66pSIwTmhHJmAMiI4UjOOpn+/2Nw1KrJiXnmid2RiEja/HAITCQ00ienxDtAhDIMAAAgAEAAIABAACAAAEBK2BfqQAAAAAAIgAgVJ3hH2Yg78qcgDmp32ctQUv4oJjoMN3ec6mS0WQX25wBAwQCAAAAAQVpUiECDLxYLw6kodDiOHpRGyavsn3GStmnKi2POJgO1JpkJvAhA50d97FlqJDgPv5UO5W0ngY2C7pY0RIZfxntgg2EDZz7IQPL2Ji2egSgcGTHSj/xC/woKvb/Y0UYit/rjnrxqcih6VOuIgYCDLxYLw6kodDiOHpRGyavsn3GStmnKi2POJgO1JpkJvAYYPOgszAAAIABAACAAQAAgAAAAADHAAAAIgYDnR33sWWokOA+/lQ7lbSeBjYLuljREhl/Ge2CDYQNnPsYH8zk3jAAAIABAACAAQAAgAAAAADHAAAAIgYDy9iYtnoEoHBkx0o/8Qv8KCr2/2NFGIrf64568anIoekY7QIQyDAAAIABAACAAQAAgAAAAADHAAAAAAA="
            }); 
            var operationRequest = new ChannelOperationRequest()
            {
                ChannelOperationRequestPsbts = channelOpReqPsbts
            };

            // Act
            var result = LightningService.GetCombinedPsbt(operationRequest);
            
            // Assert
            result.Should().NotBeNull();
        }
        
        [Fact]
        public void CreateLightningClient_EndpointIsNull()
        {
            // Act
            var act = () => LightningService.CreateLightningClient(null);
            
            // Assert
            act
                .Should()
                .Throw<ArgumentException>()
                .WithMessage("Endpoint cannot be null");
        }
        
        [Fact]
        public void CreateLightningClient_ReturnsLightningClient()
        {
            // Act
            var result = LightningService.CreateLightningClient("10.0.0.1");
            
            // Assert
            result.Should().NotBeNull();
        }
        
        [Fact]
        public async void OpenChannel_Success()
        {
            // Arrange
            Environment.SetEnvironmentVariable("NBXPLORER_URI", "http://10.0.0.2:38762");
            var dbContextFactory = new Mock<IDbContextFactory<ApplicationDbContext>>();
            
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: "ChannelOpenDb")
                .Options;
            var context = new ApplicationDbContext(options);
            dbContextFactory.Setup(x => x.CreateDbContextAsync(default)).ReturnsAsync(context);
            var channelOperationRequestRepository = new Mock<IChannelOperationRequestRepository>();
            var nodeRepository = new Mock<INodeRepository>();
           
            var channelOpReqPsbts = new List<ChannelOperationRequestPSBT>();
            var userSignedPSBT = "cHNidP8BAF4BAAAAAexSK/NStWhBu7utoQ5D5FNqMhhdhA34/nEgMU0XjbJlAAAAAAD/////AYSRNXcAAAAAIgAguNLINpkV//IIFd1ti2ig15+6mPOhNWykV0mwsneO9FcAAAAATwEENYfPAy8RJCyAAAAB/DvuQjoBjOttImoGYyiO0Pte4PqdeQqzcNAw4Ecw5sgDgI4uHNSCvdBxlpQ8WoEz0WmvhgIra7A4F3FkTsB0RNcQH8zk3jAAAIABAACAAQAAgE8BBDWHzwNWrAP0gAAAAfkIrkpmsP+hqxS1WvDOSPKnAiXLkBCQLWkBr5C5Po+BAlGvFeBbuLfqwYlbP19H/+/s2DIaAu8iKY+J0KIDffBgEGDzoLMwAACAAQAAgAEAAIBPAQQ1h88DfblGjYAAAAH1InDHaHo6+zUe9PG5owwQ87bTkhcGg66pSIwTmhHJmAMiI4UjOOpn+/2Nw1KrJiXnmid2RiEja/HAITCQ00ienxDtAhDIMAAAgAEAAIABAACAAAEBKwCUNXcAAAAAIgAgs1MYpDJWIIGz/LeRwb5D/c1wgjKmSotvf8QyY3nsEMQiAgLYVMVgz+bATgvrRDQbanlASVXtiUwPt9yCgkQfv2kssUcwRAIgLWbNccdr2EVATngtvTKY4H71ihdlTx65jsLnb7l/YLMCIDcxNFGRQcJ9NS+naFO+MRgWUnuoGWmjfPUnL7bs8Li9AgEDBAIAAAABBWlSIQLYVMVgz+bATgvrRDQbanlASVXtiUwPt9yCgkQfv2kssSEDAmf/CxGXSG9xiPljcG/e5CXFnnukFn0pJ64Q9U2aNL8hAxpTd/JawX43QWk3yFK6wOPpsRK931hHnT2R2BYwsouPU64iBgLYVMVgz+bATgvrRDQbanlASVXtiUwPt9yCgkQfv2kssRgfzOTeMAAAgAEAAIABAACAAAAAAAAAAAAiBgMCZ/8LEZdIb3GI+WNwb97kJcWee6QWfSknrhD1TZo0vxhg86CzMAAAgAEAAIABAACAAAAAAAAAAAAiBgMaU3fyWsF+N0FpN8hSusDj6bESvd9YR509kdgWMLKLjxjtAhDIMAAAgAEAAIABAACAAAAAAAAAAAAAAA==";
            channelOpReqPsbts.Add(new ChannelOperationRequestPSBT()
            {
                PSBT = userSignedPSBT,
            });
            
            var destinationNode = new Node()
            {
                PubKey = "03485d8dcdd149c87553eeb80586eb2bece874d412e9f117304446ce189955d375",
                ChannelAdminMacaroon = "def",
                Endpoint = "10.0.0.2"
            };

            var wallet = CreateWallet.CreateTestWallet();
            var operationRequest = new ChannelOperationRequest
            {
                RequestType = OperationRequestType.Open,
                SourceNode = new Node()
                {
                    PubKey = "03b48034270e522e4033afdbe43383d66d426638927b940d09a8a7a0de4d96e807",
                    ChannelAdminMacaroon = "abc",
                    Endpoint = "10.0.0.1"
                },
                DestNode = destinationNode,
                Wallet = wallet,
                ChannelOperationRequestPsbts = channelOpReqPsbts,
            };
            
            channelOperationRequestRepository
                .Setup(x => x.GetById(It.IsAny<int>()))
                .ReturnsAsync(operationRequest);
            
            var explorerClient = Interceptor.For<ExplorerClient>()
                .Setup(x => x.GetUnusedAsync(
                    Arg.Ignore<DerivationStrategyBase>(),
                    Arg.Ignore<DerivationFeature>(),
                    Arg.Ignore<int>(),
                    Arg.Ignore<bool>(),
                    Arg.Ignore<CancellationToken>()
                ))
                .Returns(new KeyPathInformation() { Address = BitcoinAddress.Create("bcrt1q590shaxaf5u08ml8jwlzghz99dup3z9592vxal", Network.RegTest) });
            
            LightningHelper.GenerateNetwork = () => (Network.RegTest, explorerClient);

            var nodes = new List<Node>();
            nodes.Add(destinationNode);
            
            nodeRepository
                .Setup(x => x.GetAllManagedByFundsManager())
                .Returns(Task.FromResult(nodes));
            
            var lightningClient = Interceptor.For<Lightning.LightningClient>()
                .Setup(x => x.GetNodeInfoAsync(
                    Arg.Ignore<NodeInfoRequest>(),
                    Arg.Ignore<Metadata>(),
                    null,
                    Arg.Ignore<CancellationToken>()
                    ))
                .Returns(MockHelpers.CreateAsyncUnaryCall(
                        new NodeInfo()
                        {
                            Node = new LightningNode()
                            {
                                Addresses = { new NodeAddress()
                                {
                                    Network = "tcp",
                                    Addr = "10.0.0.2"
                                } }
                            }
                        }));
            
            LightningService.CreateLightningClient = (_) => lightningClient;

            lightningClient
                .Setup(x => x.ConnectPeerAsync(
                    Arg.Ignore<ConnectPeerRequest>(),
                    Arg.Ignore<Metadata>(),
                    null,
                    Arg.Ignore<CancellationToken>()
                ))
                .Returns(MockHelpers.CreateAsyncUnaryCall(new ConnectPeerResponse()));

            var noneUpdate = new OpenStatusUpdate();
            var chanPendingUpdate = new OpenStatusUpdate
            {
                ChanPending = new PendingUpdate()
            };
            var chanOpenUpdate = new OpenStatusUpdate
            {
                ChanOpen = new ChannelOpenUpdate()
                {
                    ChannelPoint = new ChannelPoint()
                }
            };
            
            var psbtFundUpdate = new OpenStatusUpdate
            {
                PsbtFund = new ReadyForPsbtFunding()
                {
                    Psbt = ByteString.FromBase64(userSignedPSBT)
                }
            };
            lightningClient
                .Setup(x => x.OpenChannel(
                    Arg.Ignore<OpenChannelRequest>(),
                    Arg.Ignore<Metadata>(),
                    null,
                    Arg.Ignore<CancellationToken>()
                ))
                .Returns(MockHelpers.CreateAsyncServerStreamingCall(
                    new List<OpenStatusUpdate>()
                    {
                        noneUpdate,
                        chanPendingUpdate,
                        psbtFundUpdate,
                        chanOpenUpdate
                    }));

            channelOperationRequestRepository
                .Setup(x => x.Update(It.IsAny<ChannelOperationRequest>()))
                .Returns((true, ""));

            var userSignedPsbtParsed = PSBT.Parse(userSignedPSBT, Network.RegTest);
            var utxoChanges = new UTXOChanges();
            var input = userSignedPsbtParsed.Inputs[0];
            var utxoList = new List<UTXO>()
            {
                new UTXO()
                {  
                    Outpoint = input.PrevOut, 
                    Index = 0,
                    ScriptPubKey = input.WitnessUtxo.ScriptPubKey, 
                    KeyPath = new KeyPath("0/0"),
                },
            };
            
            utxoChanges.Confirmed = new UTXOChange() { UTXOs = utxoList };
            explorerClient
                .Setup(x => x.GetUTXOsAsync(Arg.Ignore<DerivationStrategyBase>(), Arg.Ignore<CancellationToken>()))
                .Returns(utxoChanges);
             
            // LightningService.SignPSBT = (_, _, _, _, _, _, _) => Task.FromResult(finalizedPsbt);

            var channelOperationRequestPsbtRepository = new Mock<IChannelOperationRequestPSBTRepository>();
            channelOperationRequestPsbtRepository
                .Setup(x => x.AddAsync(It.IsAny<ChannelOperationRequestPSBT>()))
                .ReturnsAsync((true, ""));

            lightningClient
                .Setup(x => x.FundingStateStep(
                    Arg.Ignore<FundingTransitionMsg>(),
                    Arg.Ignore<Metadata>(),
                    null,
                    default))
                .Returns(new FundingStateStepResp());
            
            lightningClient
                .Setup(x => x.FundingStateStepAsync(
                    Arg.Ignore<FundingTransitionMsg>(),
                    Arg.Ignore<Metadata>(),
                    null,
                    default))
                .Returns(MockHelpers.CreateAsyncUnaryCall(new FundingStateStepResp()));
            
            var lightningService = new LightningService(logger, channelOperationRequestRepository.Object, nodeRepository.Object, dbContextFactory.Object, null, null, null, channelOperationRequestPsbtRepository.Object, null, null);
            
            // Act
            var act = async () => await lightningService.OpenChannel(operationRequest);
            
            // Assert
            await act.Should().NotThrowAsync();
        }
    }
}