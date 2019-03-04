using Amazon;
using Amazon.Route53;
using Amazon.Route53.Model;
using Amazon.Runtime;
using Amazon.Runtime.CredentialManagement;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace AydenIO {
    namespace DnsUpdater {
        class AmazonRoute53Updater : IUpdater {
            private AmazonRoute53Client awsClient;
            private string hostedZoneId;

            private string domain;
            private int ttl;

            public AmazonRoute53Updater(string awsProfilesLocation, string awsProfileName, string awsRegionId, string awsHostedZoneId, string domain, int ttl = 300) {
                this.hostedZoneId = awsHostedZoneId;
                this.domain = domain;
                this.ttl = ttl;

                // Load credentials
                CredentialProfileStoreChain storeChain = new CredentialProfileStoreChain(awsProfilesLocation);
                AWSCredentials defaultCredentials;

                if (!storeChain.TryGetAWSCredentials(awsProfileName, out defaultCredentials)) {
                    throw new AmazonClientException("Unable to find a default profile in CredentialProfileStoreChain.");
                }

                // Create an Amazon Route 53 client object
                this.awsClient = new AmazonRoute53Client(defaultCredentials, RegionEndpoint.GetBySystemName(awsRegionId));
            }

            public async Task Update(GetExternalIpAddressHandler getExternalIp) {
                // Get external IP
                IPAddress externalIp = await getExternalIp();

                Console.WriteLine("External ip is {0}", externalIp.ToString());

                // Create a hosted zone
                string referenceId = "autoUpdater3-" + Guid.NewGuid().ToString();

                Console.WriteLine("{0} is {1}", nameof(referenceId), referenceId);

                GetHostedZoneRequest zoneRequest = new GetHostedZoneRequest() {
                    Id = this.hostedZoneId
                };

                GetHostedZoneResponse zoneResponse = await this.awsClient.GetHostedZoneAsync(zoneRequest);

                // Create a resource record set change batch
                ResourceRecordSet recordSet;

                Console.WriteLine("Address type is {0}", externalIp.AddressFamily.ToString());

                if (externalIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) {
                    recordSet = new ResourceRecordSet() {
                        Name = this.domain,
                        TTL = this.ttl,
                        Type = RRType.A,
                        ResourceRecords = new List<ResourceRecord>() {
                        new ResourceRecord() { Value = externalIp.ToString() }
                    }
                    };
                } else {
                    recordSet = new ResourceRecordSet() {
                        Name = this.domain,
                        TTL = this.ttl,
                        Type = RRType.AAAA,
                        ResourceRecords = new List<ResourceRecord>() {
                        new ResourceRecord() { Value = externalIp.ToString() }
                    }
                    };
                }

                Change change1 = new Change() {
                    ResourceRecordSet = recordSet,
                    Action = ChangeAction.UPSERT
                };

                ChangeBatch changeBatch = new ChangeBatch() {
                    Changes = new List<Change>() {
                    change1
                }
                };

                // Update the zone's resource record sets
                ChangeResourceRecordSetsRequest recordSetRequest = new ChangeResourceRecordSetsRequest() {
                    HostedZoneId = zoneResponse.HostedZone.Id,
                    ChangeBatch = changeBatch
                };

                ChangeResourceRecordSetsResponse recordSetResponse = await this.awsClient.ChangeResourceRecordSetsAsync(recordSetRequest);

                // Monitor the change status
                GetChangeRequest changeRequest = new GetChangeRequest() {
                    Id = recordSetResponse.ChangeInfo.Id
                };

                Console.Write("Change is pending");

                while (true) {
                    GetChangeResponse changeResponse = await this.awsClient.GetChangeAsync(changeRequest);

                    if (changeResponse.ChangeInfo.Status == ChangeStatus.INSYNC) {
                        break;
                    }

                    Console.Write(".");

                    await Task.Delay(1000);
                }

                Console.WriteLine("");
                Console.WriteLine("Change is complete.");
            }
        }
    }
}
