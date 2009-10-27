using System;
using System.Linq;
using System.Transactions;
using NCommon.Data.NHibernate.Tests.Domain;
using NUnit.Framework;

namespace NCommon.Data.NHibernate.Tests
{
	/// <summary>
	/// Specialized test cases that test issues with transaction management in the NHUnitOfWork implementation.
	/// </summary>
	[TestFixture]
	public class NHUnitOfWorkTransactionTests : NHTestBase
	{
		[Test]
		public void changes_are_persisted_when_ambient_scope_is_committed()
		{
			var customerName = string.Empty;
			using (var ambientScope = new TransactionScope())
			{
				using (var scope = new UnitOfWorkScope())
				{
					var customer = new NHRepository<Customer>().First();
					customerName = customer.FirstName;
					customer.FirstName = "Changed";
					scope.Commit();
				}
				ambientScope.Complete();
			} 

			using (var scope = new UnitOfWorkScope())
			{
				var customer = new NHRepository<Customer>().First();
				Assert.That(customer.FirstName, Is.EqualTo("Changed"));
				customer.FirstName = customerName;
				scope.Commit();
			}
		}

		[Test]
		public void changes_are_not_persisted_when_ambient_transaction_rolls_back()
		{
			var customerName = string.Empty;
			using (var ambientScope = new TransactionScope())
			{
				using (var scope = new UnitOfWorkScope())
				{
					var customer = new NHRepository<Customer>().First();
					customerName = customer.FirstName;
					customer.FirstName = "Changed";
					scope.Commit();
				} 
			} //Auto rollback

			using (var scope = new UnitOfWorkScope())
			{
				var customer = new NHRepository<Customer>().First();
				Assert.That(customer.FirstName, Is.EqualTo(customerName));
			}
		}

		[Test]
		public void when_ambient_transaction_is_running_multiple_scopes_work()
		{
			using (var ambientScope = new TransactionScope())
			{
				using (var firstUOW = new UnitOfWorkScope())
				{
					var repository = new NHRepository<Customer>();
					var query = repository.Where(x => x.Address.State == "LA");
					Assert.That(query.Count(), Is.GreaterThan(0));
					firstUOW.Commit();
				}

				using(var secondUOW = new UnitOfWorkScope())
				{
					var repository = new NHRepository<Customer>();
					repository.Add(new Customer
					{
						FirstName = "NHUnitOfWorkTransactionTest",
						LastName = "Customer",
						Address = new Address
						{
							StreetAddress1 = "This recrd was insertd via a test",
							City = "Fictional City",
							State = "LA",
							ZipCode = "00000"
						}
					});
					secondUOW.Commit();
				}
				ambientScope.Complete(); //Completing the abient scope.
			}
		}

		[Test]
		public void when_ambient_transaction_is_running_and_a_previous_scope_rollsback_new_scope_still_works()
		{
			var oldCustomerName = string.Empty;
			var newCustomerName = "NewCustomer" + new Random().Next(0, int.MaxValue);

			using (var ambientScope = new TransactionScope())
			{
				using (var firstUOW = new UnitOfWorkScope())
				{
					var customer = new NHRepository<Customer>().First();
					oldCustomerName = customer.FirstName;
					customer.FirstName = "Changed";
				}  //Rollback

				using (var secondUOW = new UnitOfWorkScope())
				{
					new NHRepository<Customer>().Add(new Customer
					{
						FirstName = newCustomerName,
						LastName = "Save",
						Address = new Address
						{
							StreetAddress1 = "This record was inserted via a test",
							City = "Fictional City",
							ZipCode = "00000"
						}
					});
					secondUOW.Commit();
				}
			}

			using (var scope = new UnitOfWorkScope())
			{
				var repository = new NHRepository<Customer>();
				Assert.That(repository.First().FirstName, Is.EqualTo(oldCustomerName));
				Assert.That(repository.Where(x => x.FirstName == newCustomerName).Count(), Is.GreaterThan(0));
			}
		}

		[Test]
		public void NHUOW_Issue_6_Replication ()
		{
			var readCustomerFunc = new Func<Customer>(() =>
			{
				using (var scope = new UnitOfWorkScope())
				{
					var customer = new NHRepository<Customer>().First();
					scope.Commit();
					return customer;
				}
			});

			var updateCustomerFunc = new Func<Customer, Customer>(customer =>
			{
				using (var scope = new UnitOfWorkScope())
				{
					var repository = new NHRepository<Customer>();
					repository.Attach(customer);
					scope.Commit();
					return customer;
				}
			});

			var newCustomerName = "Changed" + new Random().Next(0, int.MaxValue);
			using (var masterScope = new UnitOfWorkScope())
			{
				using (var childScope = new UnitOfWorkScope(UnitOfWorkScopeTransactionOptions.CreateNew))
				{
					var customer = readCustomerFunc();
					customer.FirstName = newCustomerName;
					updateCustomerFunc(customer);
					childScope.Commit();
				}
			} //Rollback

			var checkCustomer = readCustomerFunc();
			Assert.That(checkCustomer.FirstName, Is.EqualTo(newCustomerName));
		}
	}
}